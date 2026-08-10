namespace ComfyQuestLab;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

/// <summary>Startup policy for the durable canonical-event archive.
///
/// Kept free of BepInEx and Unity so the actual file contract can be exercised by the
/// headless test project. Values are captured once at startup: changing privacy or
/// retention policy in the middle of a file would make its session header untrue.</summary>
public sealed class LabEventArchiveOptions {
  public string DirectoryPath;
  public string ReleaseId;
  public string RuntimeProfile;
  public bool IncludeDetails;
  public bool IncludeDiagnosticIdentity;
  public double JsonlFlushSeconds = 1.0;
  public double CsvFlushSeconds = 5.0;
  public int MaxSegmentBytes = 16 * 1024 * 1024;
  public int MaxSegments = 24;
  public int QueueCapacity = 4096;

  /// <summary>Tests may pin time/session naming or gate the writer before startup.
  /// Production leaves all three null.</summary>
  public Func<DateTime> UtcNow;
  public string SessionIdOverride;
  public Action BeforeWriterStart;
}

/// <summary>Durable, bounded, privacy-aware event archive.
///
/// <see cref="TryRecord"/> only sanitizes and enqueues. A background worker owns every
/// stream and all filesystem I/O, so a slow disk cannot delay a Valheim postfix or alter
/// quest matching. The queue is deliberately bounded: overload drops archive rows and
/// reports the count in the clean session summary rather than growing memory forever.
/// JSONL is authoritative. CSV is an optional, identically rotated projection intended
/// for spreadsheets and flushed at its own interval.</summary>
public sealed class LabEventArchive : IDisposable {
  public const string Schema = "comfy-questlab-events/v1";
  public const string CsvHeader =
      "schema,session_id,sequence,timestamp_utc,school,creator_event,target,detail,"
      + "usability,diagnostic_seam,action_identity";

  sealed class PendingEvent {
    public long Sequence;
    public string TimestampUtc;
    public string School;
    public string CreatorEvent;
    public string Target;
    public string Detail;
    public string Usability;
    public string DiagnosticSeam;
    public string ActionIdentity;
  }

  readonly LabEventArchiveOptions _options;
  readonly Queue<PendingEvent> _pending = new Queue<PendingEvent>();
  readonly object _gate = new object();
  readonly AutoResetEvent _wake = new AutoResetEvent(false);
  readonly Thread _worker;
  readonly Func<DateTime> _utcNow;
  readonly DateTime _startedUtc;
  readonly string _startedUtcText;
  readonly string _sessionId;

  volatile bool _stopping;
  volatile bool _forceFlush;
  volatile string _fault;
  volatile string _csvFault;
  long _nextSequence;
  long _accepted;
  long _written;
  long _dropped;
  int _segment = 1;
  long _jsonlBytes;
  long _csvBytes;
  int _segmentEvents;
  StreamWriter _jsonl;
  readonly List<string> _csvRows = new List<string>();
  string _csvPath;
  DateTime _nextJsonlFlushUtc;
  DateTime _nextCsvFlushUtc;
  string _currentJsonlPath;
  long _notifiedDrops;

  public LabEventArchive(LabEventArchiveOptions options) {
    if (options == null) throw new ArgumentNullException(nameof(options));
    if (string.IsNullOrWhiteSpace(options.DirectoryPath)) {
      throw new ArgumentException("archive directory is required", nameof(options));
    }
    _options = Normalize(options);
    _utcNow = _options.UtcNow ?? delegate { return DateTime.UtcNow; };
    _startedUtc = AsUtc(_utcNow());
    _startedUtcText = Iso(_startedUtc);

    Directory.CreateDirectory(_options.DirectoryPath);
    _sessionId = string.IsNullOrWhiteSpace(_options.SessionIdOverride)
        ? UniqueSessionId(_startedUtc, _options.ReleaseId, _options.RuntimeProfile)
        : SafeToken(_options.SessionIdOverride, 96, "session");

    _worker = new Thread(WorkerLoop) {
      IsBackground = true,
      Name = "ComfyQuestLab event archive",
    };
    _worker.Start();
  }

  public string SessionId { get { return _sessionId; } }
  public string CurrentJsonlPath { get { lock (_gate) { return _currentJsonlPath; } } }
  public long AcceptedCount { get { return Interlocked.Read(ref _accepted); } }
  public long WrittenCount { get { return Interlocked.Read(ref _written); } }
  public long DroppedCount { get { return Interlocked.Read(ref _dropped); } }
  public string Fault { get { return _fault; } }

  /// <summary>Enqueue one row without touching the filesystem. False means the bounded
  /// queue was full or the archive is shutting down; gameplay should simply continue.</summary>
  public bool TryRecord(LabEvent row, string actionIdentity = null) {
    try {
      if (_stopping) return false;
      if (_fault != null) {
        Interlocked.Increment(ref _dropped);
        return false;
      }

      // The live ring deliberately exposes an unknown signature so drift is diagnosable.
      // The durable creatorEvent column must remain stable vocabulary, though: raw runtime
      // identifiers belong only behind the diagnostic-identity opt-in.
      bool unclassified = string.Equals(
              row.Usability, LabUsability.LabCandidate, StringComparison.OrdinalIgnoreCase)
          && string.Equals(row.EventName, row.Seam, StringComparison.Ordinal);
      var pending = new PendingEvent {
        TimestampUtc = Iso(AsUtc(_utcNow())),
        School = Clean(row.Category, 32),
        CreatorEvent = unclassified ? "unclassified_runtime_event" : Clean(row.EventName, 96),
        Target = Clean(row.Target, 256),
        Detail = _options.IncludeDetails ? Clean(row.Detail, 512) : string.Empty,
        Usability = Clean(row.Usability, 64),
        DiagnosticSeam = _options.IncludeDiagnosticIdentity ? Clean(row.Seam, 512) : string.Empty,
        ActionIdentity = _options.IncludeDiagnosticIdentity
            ? Clean(actionIdentity, 256)
            : string.Empty,
      };

      lock (_gate) {
        if (_stopping || _pending.Count >= _options.QueueCapacity) {
          Interlocked.Increment(ref _dropped);
          return false;
        }
        pending.Sequence = ++_nextSequence;
        _pending.Enqueue(pending);
        Interlocked.Increment(ref _accepted);
      }
      _wake.Set();
      return true;
    } catch (Exception) {
      // This method executes below game hooks. Archiving may lose a row; gameplay may not.
      Interlocked.Increment(ref _dropped);
      return false;
    }
  }

  /// <summary>Ask the writer to expose all currently queued rows. Never waits for disk.</summary>
  public void RequestFlush() {
    _forceFlush = true;
    _wake.Set();
  }

  public string Status() {
    string path = CurrentJsonlPath;
    string state = _fault != null
        ? "faulted"
        : (_stopping ? (_worker.IsAlive ? "closing" : "closed") : "active");
    return "event archive " + state
        + " · session " + _sessionId
        + " · accepted " + AcceptedCount.ToString(CultureInfo.InvariantCulture)
        + " · written " + WrittenCount.ToString(CultureInfo.InvariantCulture)
        + " · dropped " + DroppedCount.ToString(CultureInfo.InvariantCulture)
        + (string.IsNullOrWhiteSpace(path) ? string.Empty : " · " + path)
        + (_fault == null ? string.Empty : " · " + _fault)
        + (_csvFault == null ? string.Empty : " · CSV warning " + _csvFault);
  }

  public void Dispose() {
    if (_stopping) return;
    _stopping = true;
    _wake.Set();
    // A clean summary is best-effort. Never hold Valheim shutdown indefinitely on a disk.
    _worker.Join(3000);
  }

  void WorkerLoop() {
    try {
      if (_options.BeforeWriterStart != null) _options.BeforeWriterStart();
      OpenSegment();
      while (true) {
        List<PendingEvent> batch = Drain();
        foreach (PendingEvent item in batch) Write(item);

        DateTime now = AsUtc(_utcNow());
        WriteDropNotice(now);
        bool forced = _forceFlush;
        _forceFlush = false;
        if (forced || now >= _nextJsonlFlushUtc) {
          _jsonl.Flush();
          _nextJsonlFlushUtc = now.AddSeconds(_options.JsonlFlushSeconds);
        }
        if (_options.CsvFlushSeconds > 0 && (forced || now >= _nextCsvFlushUtc)) {
          WriteCsvSnapshot();
          _nextCsvFlushUtc = now.AddSeconds(_options.CsvFlushSeconds);
        }

        if (_stopping && PendingCount() == 0) {
          WriteSessionEnd(now);
          _jsonl.Flush();
          if (_options.CsvFlushSeconds > 0) WriteCsvSnapshot();
          break;
        }
        _wake.WaitOne(100);
      }
    } catch (Exception exception) {
      _fault = exception.GetType().Name + ": " + exception.Message;
    } finally {
      CloseWriters();
    }
  }

  List<PendingEvent> Drain() {
    var batch = new List<PendingEvent>();
    lock (_gate) {
      while (_pending.Count > 0) batch.Add(_pending.Dequeue());
    }
    return batch;
  }

  int PendingCount() {
    lock (_gate) return _pending.Count;
  }

  void Write(PendingEvent item) {
    string json = EventJson(item);
    string csv = _options.CsvFlushSeconds <= 0 ? null : EventCsv(item);
    long jsonBytes = Utf8Bytes(json + "\n");
    long csvBytes = csv == null ? 0 : Utf8Bytes(csv + "\n");
    if (_segmentEvents > 0
        && (_jsonlBytes + jsonBytes > _options.MaxSegmentBytes
            || (csv != null && _csvBytes + csvBytes > _options.MaxSegmentBytes))) {
      Rotate();
    }

    _jsonl.WriteLine(json);
    _jsonlBytes += jsonBytes;
    if (csv != null) {
      _csvRows.Add(csv);
      _csvBytes += csvBytes;
    }
    _segmentEvents++;
    Interlocked.Increment(ref _written);
  }

  void Rotate() {
    _jsonl.Flush();
    if (_options.CsvFlushSeconds > 0) WriteCsvSnapshot();
    CloseWriters();
    _segment++;
    _segmentEvents = 0;
    OpenSegment();
  }

  void OpenSegment() {
    string stem = "questlab-events-" + _sessionId
        + (_segment == 1 ? string.Empty : "-part" + _segment.ToString("000", CultureInfo.InvariantCulture));
    string jsonlPath = Path.Combine(_options.DirectoryPath, stem + ".jsonl");
    _jsonl = NewWriter(jsonlPath);
    lock (_gate) _currentJsonlPath = jsonlPath;
    string header = SessionHeaderJson();
    _jsonl.WriteLine(header);
    _jsonlBytes = Utf8Bytes(header + "\n");

    _csvRows.Clear();
    if (_options.CsvFlushSeconds > 0) {
      _csvPath = Path.Combine(_options.DirectoryPath, stem + ".csv");
      _csvBytes = Utf8Bytes(CsvHeader + "\n");
    } else {
      _csvPath = null;
      _csvBytes = 0;
    }

    DateTime now = AsUtc(_utcNow());
    _nextJsonlFlushUtc = now.AddSeconds(_options.JsonlFlushSeconds);
    _nextCsvFlushUtc = now.AddSeconds(Math.Max(0.1, _options.CsvFlushSeconds));
    PurgeOldSegments();
  }

  static StreamWriter NewWriter(string path) {
    var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
    var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096);
    writer.NewLine = "\n";
    return writer;
  }

  void CloseWriters() {
    try { if (_jsonl != null) _jsonl.Dispose(); } catch (Exception) { }
    _jsonl = null;
  }

  /// <summary>Publish a complete current-segment CSV in one rename. Readers see either
  /// the previous snapshot or the new one, never a half-written table.</summary>
  void WriteCsvSnapshot() {
    if (string.IsNullOrWhiteSpace(_csvPath)) return;
    string temporary = _csvPath + ".tmp";
    try {
      using (var stream = new FileStream(
          temporary, FileMode.Create, FileAccess.Write, FileShare.None))
      using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096)) {
        writer.NewLine = "\n";
        writer.WriteLine(CsvHeader);
        foreach (string row in _csvRows) writer.WriteLine(row);
        writer.Flush();
        stream.Flush(true);
      }
      if (File.Exists(_csvPath)) File.Replace(temporary, _csvPath, null);
      else File.Move(temporary, _csvPath);
      _csvFault = null;
    } catch (Exception exception) {
      // A spreadsheet reader can temporarily hold the projection open. Preserve JSONL and
      // retry the atomic snapshot on the next interval instead of faulting the archive.
      _csvFault = exception.GetType().Name + ": " + exception.Message;
    } finally {
      TryDelete(temporary);
    }
  }

  void PurgeOldSegments() {
    try {
      string[] jsonl = Directory.GetFiles(_options.DirectoryPath, "questlab-events-*.jsonl");
      Array.Sort(jsonl, delegate(string left, string right) {
        return File.GetLastWriteTimeUtc(left).CompareTo(File.GetLastWriteTimeUtc(right));
      });
      int excess = jsonl.Length - _options.MaxSegments;
      for (int i = 0; i < excess; i++) {
        string candidate = jsonl[i];
        if (string.Equals(candidate, _currentJsonlPath, StringComparison.OrdinalIgnoreCase)) continue;
        string csv = Path.ChangeExtension(candidate, ".csv");
        TryDelete(csv + ".tmp");
        // A spreadsheet can hold the derived CSV open. Keep the authoritative JSONL so
        // this pair remains discoverable and a future purge retries it; deleting JSONL
        // first would orphan a locked CSV forever because retention enumerates JSONL.
        if (!TryDelete(csv)) continue;
        TryDelete(candidate);
      }
    } catch (Exception) {
      // Retention cleanup is opportunistic. A locked old export must not disable new capture.
    }
  }

  static bool TryDelete(string path) {
    try {
      if (File.Exists(path)) File.Delete(path);
      return !File.Exists(path);
    } catch (Exception) {
      return false;
    }
  }

  void WriteSessionEnd(DateTime endedUtc) {
    var sb = new StringBuilder(320);
    sb.Append('{');
    JsonField(sb, "schema", Schema, false);
    JsonField(sb, "recordType", "sessionEnd", true);
    JsonField(sb, "sessionId", _sessionId, true);
    JsonField(sb, "releaseId", _options.ReleaseId, true);
    JsonField(sb, "runtimeProfile", _options.RuntimeProfile, true);
    JsonField(sb, "runtimeProfileSemantics", "startup-default", true);
    JsonField(sb, "startedUtc", _startedUtcText, true);
    JsonField(sb, "endedUtc", Iso(endedUtc), true);
    JsonNumber(sb, "eventCount", WrittenCount, true);
    JsonNumber(sb, "droppedEventCount", DroppedCount, true);
    JsonNumber(sb, "segments", _segment, true);
    JsonField(sb, "reason", "clean-shutdown", true);
    sb.Append('}');
    string line = sb.ToString();
    long bytes = Utf8Bytes(line + "\n");
    if (_segmentEvents > 0 && _jsonlBytes + bytes > _options.MaxSegmentBytes) {
      Rotate();
      WriteSessionEnd(endedUtc); // rebuild so the summary's segment count includes this part
      return;
    }
    _jsonl.WriteLine(line);
    _jsonlBytes += bytes;
  }

  void WriteDropNotice(DateTime atUtc) {
    long total = DroppedCount;
    if (total <= _notifiedDrops) return;
    long sinceLast = total - _notifiedDrops;
    var sb = new StringBuilder(256);
    sb.Append('{');
    JsonField(sb, "schema", Schema, false);
    JsonField(sb, "recordType", "archiveNotice", true);
    JsonField(sb, "sessionId", _sessionId, true);
    JsonField(sb, "timestampUtc", Iso(atUtc), true);
    JsonField(sb, "reason", "queue-capacity", true);
    JsonNumber(sb, "droppedSinceLastNotice", sinceLast, true);
    JsonNumber(sb, "totalDroppedEventCount", total, true);
    sb.Append('}');
    string line = sb.ToString();
    long bytes = Utf8Bytes(line + "\n");
    if (_segmentEvents > 0 && _jsonlBytes + bytes > _options.MaxSegmentBytes) Rotate();
    _jsonl.WriteLine(line);
    _jsonlBytes += bytes;
    _segmentEvents++;
    _notifiedDrops = total;
  }

  string SessionHeaderJson() {
    var sb = new StringBuilder(320);
    sb.Append('{');
    JsonField(sb, "schema", Schema, false);
    JsonField(sb, "recordType", "session", true);
    JsonField(sb, "sessionId", _sessionId, true);
    JsonField(sb, "startedUtc", _startedUtcText, true);
    JsonField(sb, "releaseId", _options.ReleaseId, true);
    JsonField(sb, "runtimeProfile", _options.RuntimeProfile, true);
    JsonField(sb, "runtimeProfileSemantics", "startup-default", true);
    JsonNumber(sb, "segment", _segment, true);
    sb.Append(",\"fields\":{\"details\":")
        .Append(_options.IncludeDetails ? "true" : "false")
        .Append(",\"diagnosticIdentity\":")
        .Append(_options.IncludeDiagnosticIdentity ? "true" : "false")
        .Append("}}");
    return sb.ToString();
  }

  string EventJson(PendingEvent item) {
    var sb = new StringBuilder(512);
    sb.Append('{');
    JsonField(sb, "schema", Schema, false);
    JsonField(sb, "recordType", "event", true);
    JsonField(sb, "sessionId", _sessionId, true);
    JsonNumber(sb, "sequence", item.Sequence, true);
    JsonField(sb, "timestampUtc", item.TimestampUtc, true);
    JsonField(sb, "school", item.School, true);
    JsonField(sb, "creatorEvent", item.CreatorEvent, true);
    JsonField(sb, "target", item.Target, true);
    JsonField(sb, "usability", item.Usability, true);
    if (_options.IncludeDetails) JsonField(sb, "detail", item.Detail, true);
    if (_options.IncludeDiagnosticIdentity) {
      JsonField(sb, "diagnosticSeam", item.DiagnosticSeam, true);
      JsonField(sb, "actionIdentity", item.ActionIdentity, true);
    }
    return sb.Append('}').ToString();
  }

  string EventCsv(PendingEvent item) {
    return Csv(Schema) + ',' + Csv(_sessionId) + ','
        + Csv(item.Sequence.ToString(CultureInfo.InvariantCulture)) + ','
        + Csv(item.TimestampUtc) + ',' + Csv(item.School) + ',' + Csv(item.CreatorEvent) + ','
        + Csv(item.Target) + ',' + Csv(item.Detail) + ',' + Csv(item.Usability) + ','
        + Csv(item.DiagnosticSeam) + ',' + Csv(item.ActionIdentity);
  }

  static string Csv(string value) {
    value = value ?? string.Empty;
    // CSV is meant for Excel and Google Sheets. Make every text cell inert before RFC 4180
    // quoting so an event target can never become a formula when somebody opens the export.
    if (LooksLikeSpreadsheetFormula(value)) value = "'" + value;
    if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0) return value;
    return "\"" + value.Replace("\"", "\"\"") + "\"";
  }

  static bool LooksLikeSpreadsheetFormula(string value) {
    int index = 0;
    while (index < value.Length && (char.IsWhiteSpace(value[index]) || char.IsControl(value[index]))) {
      index++;
    }
    if (index >= value.Length) return false;
    char first = value[index];
    return first == '=' || first == '+' || first == '-' || first == '@';
  }

  static void JsonField(StringBuilder sb, string name, string value, bool comma) {
    if (comma) sb.Append(',');
    sb.Append('"').Append(name).Append("\":").Append(Json(value));
  }

  static void JsonNumber(StringBuilder sb, string name, long value, bool comma) {
    if (comma) sb.Append(',');
    sb.Append('"').Append(name).Append("\":")
        .Append(value.ToString(CultureInfo.InvariantCulture));
  }

  static string Json(string value) {
    var sb = new StringBuilder((value ?? string.Empty).Length + 8).Append('"');
    foreach (char ch in value ?? string.Empty) {
      switch (ch) {
        case '"': sb.Append("\\\""); break;
        case '\\': sb.Append("\\\\"); break;
        case '\b': sb.Append("\\b"); break;
        case '\f': sb.Append("\\f"); break;
        case '\n': sb.Append("\\n"); break;
        case '\r': sb.Append("\\r"); break;
        case '\t': sb.Append("\\t"); break;
        default:
          if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4"));
          else sb.Append(ch);
          break;
      }
    }
    return sb.Append('"').ToString();
  }

  static string Clean(string value, int maxLength) {
    value = (value ?? string.Empty).Trim();
    if (value.Length > maxLength) value = value.Substring(0, maxLength - 1) + "…";
    return value;
  }

  static LabEventArchiveOptions Normalize(LabEventArchiveOptions source) {
    return new LabEventArchiveOptions {
      DirectoryPath = Path.GetFullPath(source.DirectoryPath),
      ReleaseId = Clean(source.ReleaseId, 96),
      RuntimeProfile = SafeToken(source.RuntimeProfile, 24, "unknown"),
      IncludeDetails = source.IncludeDetails,
      IncludeDiagnosticIdentity = source.IncludeDiagnosticIdentity,
      JsonlFlushSeconds = Math.Max(0.1, Math.Min(60, source.JsonlFlushSeconds)),
      CsvFlushSeconds = source.CsvFlushSeconds <= 0
          ? 0
          : Math.Max(0.1, Math.Min(3600, source.CsvFlushSeconds)),
      MaxSegmentBytes = Math.Max(4096, Math.Min(256 * 1024 * 1024, source.MaxSegmentBytes)),
      MaxSegments = Math.Max(2, Math.Min(200, source.MaxSegments)),
      QueueCapacity = Math.Max(16, Math.Min(65536, source.QueueCapacity)),
      UtcNow = source.UtcNow,
      SessionIdOverride = source.SessionIdOverride,
      BeforeWriterStart = source.BeforeWriterStart,
    };
  }

  string UniqueSessionId(DateTime started, string releaseId, string profile) {
    string release = ReleaseShort(releaseId);
    string root = started.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture)
        + "-" + release + "-startup-" + SafeToken(profile, 24, "unknown");
    for (int collision = 1; collision <= 999; collision++) {
      string candidate = root + (collision == 1
          ? string.Empty
          : "-" + collision.ToString("00", CultureInfo.InvariantCulture));
      if (Directory.GetFiles(_options.DirectoryPath, "questlab-events-" + candidate + "*").Length == 0) {
        return candidate;
      }
    }
    throw new IOException("could not allocate a unique event archive session id");
  }

  static string ReleaseShort(string releaseId) {
    string[] parts = (releaseId ?? string.Empty).Split('-');
    for (int i = parts.Length - 1; i >= 0; i--) {
      string part = parts[i];
      if (part.Length >= 2 && (part[0] == 'r' || part[0] == 'R')) {
        bool digits = true;
        for (int j = 1; j < part.Length; j++) digits &= char.IsDigit(part[j]);
        if (digits) return part.ToLowerInvariant();
      }
    }
    return SafeToken(releaseId, 24, "dev");
  }

  static string SafeToken(string value, int maxLength, string fallback) {
    var sb = new StringBuilder();
    foreach (char ch in (value ?? string.Empty).ToLowerInvariant()) {
      if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9')) sb.Append(ch);
      else if ((ch == '-' || ch == '_') && sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
      if (sb.Length >= maxLength) break;
    }
    string token = sb.ToString().Trim('-');
    return token.Length == 0 ? fallback : token;
  }

  static DateTime AsUtc(DateTime value) {
    if (value.Kind == DateTimeKind.Utc) return value;
    if (value.Kind == DateTimeKind.Unspecified) return DateTime.SpecifyKind(value, DateTimeKind.Utc);
    return value.ToUniversalTime();
  }

  static string Iso(DateTime value) {
    return AsUtc(value).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
  }

  static long Utf8Bytes(string value) { return Encoding.UTF8.GetByteCount(value); }
}
