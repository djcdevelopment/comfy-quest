namespace ComfyQuestContracts;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

public sealed class RuntimeEvent {
  public string Name { get; set; }
  public string Target { get; set; }
  public string SourceId { get; set; }
  public DateTimeOffset At { get; set; }
  public IReadOnlyDictionary<string,string> Fields { get; set; }
  [JsonIgnore] public string DedupeKey { get; set; }
}

public sealed class TriggerProgress {
  public int Current { get; set; }
  public int Required { get; set; }
  public bool Complete { get { return Required > 0 && Current >= Required; } }
}

public sealed class TriggerWhereTrace {
  [JsonProperty("field")] public string Field { get; set; }
  [JsonProperty("expected")] public string Expected { get; set; }
  [JsonProperty("actual", NullValueHandling=NullValueHandling.Ignore)] public string Actual { get; set; }
  [JsonProperty("satisfied")] public bool Satisfied { get; set; }
}

public sealed class TriggerClauseTrace {
  [JsonProperty("op")] public string Op { get; set; }
  [JsonProperty("event", NullValueHandling=NullValueHandling.Ignore)] public string Event { get; set; }
  [JsonProperty("target", NullValueHandling=NullValueHandling.Ignore)] public string Target { get; set; }
  [JsonProperty("satisfied")] public bool Satisfied { get; set; }
  [JsonProperty("current")] public int Current { get; set; }
  [JsonProperty("required")] public int Required { get; set; }
  [JsonProperty("sequence_index", NullValueHandling=NullValueHandling.Ignore)] public int? SequenceIndex { get; set; }
  [JsonProperty("within_seconds", NullValueHandling=NullValueHandling.Ignore)] public int? WithinSeconds { get; set; }
  [JsonProperty("where")] public List<TriggerWhereTrace> Where { get; set; } = new();
  [JsonProperty("children")] public List<TriggerClauseTrace> Children { get; set; } = new();
  [JsonProperty("truncated", NullValueHandling=NullValueHandling.Ignore)] public bool? Truncated { get; set; }
}

public sealed class RejectedTransitionEvidence {
  [JsonProperty("transition_id")] public string TransitionId { get; set; }
  [JsonProperty("evidence")] public TriggerClauseTrace Evidence { get; set; }
}

public static class TriggerEvaluator {
  public const int MaxTraceSerializedBytes = 8 * 1024;
  public const int MaxTraceWhereEntries = 8;
  public static bool Matches(TriggerExpression expression, IReadOnlyList<RuntimeEvent> history) {
    if (expression == null) return false;
    history ??= Array.Empty<RuntimeEvent>();
    var bounded = expression.WithinSeconds.HasValue && history.Count > 0
      ? history.Where(x => x.At >= history[history.Count-1].At.AddSeconds(-expression.WithinSeconds.Value)).ToArray()
      : history.ToArray();
    return Eval(expression, bounded);
  }
  public static TriggerClauseTrace Explain(TriggerExpression expression, IReadOnlyList<RuntimeEvent> history) {
    history ??= Array.Empty<RuntimeEvent>();
    var bounded = expression?.WithinSeconds.HasValue == true && history.Count > 0
      ? history.Where(x => x.At >= history[history.Count - 1].At.AddSeconds(-expression.WithinSeconds.Value)).ToArray()
      : history.ToArray();
    return BuildBoundedTrace(expression, bounded);
  }
  public static TriggerProgress Measure(TriggerExpression expression, IReadOnlyList<RuntimeEvent> history) {
    history ??= Array.Empty<RuntimeEvent>();
    if (expression == null) return new TriggerProgress { Current = 0, Required = 1 };
    var bounded = expression.WithinSeconds.HasValue && history.Count > 0
      ? history.Where(x => x.At >= history[history.Count - 1].At.AddSeconds(-expression.WithinSeconds.Value)).ToArray()
      : history.ToArray();
    if (string.Equals(expression.Op, "COUNT", StringComparison.OrdinalIgnoreCase)
        && expression.Children?.Count == 1) {
      var required = Math.Max(1, expression.Count.GetValueOrDefault());
      var current = bounded.Count(value => EventMatches(expression.Children[0], value));
      return new TriggerProgress { Current = Math.Min(current, required), Required = required };
    }
    return new TriggerProgress { Current = Eval(expression, bounded) ? 1 : 0, Required = 1 };
  }
  static bool Eval(TriggerExpression x, IReadOnlyList<RuntimeEvent> h) {
    if (x == null) return false;
    var op=(x.Op??"").ToUpperInvariant();
    if(op=="EVENT") return h.Any(v=>EventMatches(x,v));
    var c=x.Children??new();
    if(op=="ANY") return c.Any(v=>Eval(v,h));
    if(op=="ALL") return c.All(v=>Eval(v,h));
    if(op=="COUNT") return c.Count==1 && h.Count(v=>EventMatches(c[0],v))>=x.Count.GetValueOrDefault();
    if(op=="SEQUENCE") { int i=0; foreach(var v in h) if(i<c.Count&&EventMatches(c[i],v)) i++; return i==c.Count; }
    return false;
  }
  static TriggerClauseTrace BuildBoundedTrace(TriggerExpression expression,IReadOnlyList<RuntimeEvent> history) {
    var nodeCount=CountNodes(expression);
    var full=BuildTrace(expression,history,nodeCount,128,out _);
    if(TraceBytes(full)<=MaxTraceSerializedBytes)return full;
    TriggerClauseTrace best=null;var bestNodes=0;var bestCharacters=0;
    for(var maxCharacters=128;maxCharacters>=8;maxCharacters/=2) {
      var low=1;var high=nodeCount;TriggerClauseTrace fit=null;var fitNodes=0;
      while(low<=high) {
        var budget=low+(high-low)/2;
        var candidate=BuildTrace(expression,history,budget,maxCharacters,out var used);
        candidate.Truncated=true;
        if(TraceBytes(candidate)<=MaxTraceSerializedBytes){fit=candidate;fitNodes=used;low=budget+1;}else high=budget-1;
      }
      if(fit!=null&&(fitNodes>bestNodes||(fitNodes==bestNodes&&maxCharacters>bestCharacters))){best=fit;bestNodes=fitNodes;bestCharacters=maxCharacters;}
    }
    return best??new TriggerClauseTrace{Op="",Satisfied=false,Current=0,Required=1,Truncated=true};
  }
  static TriggerClauseTrace BuildTrace(TriggerExpression expression,IReadOnlyList<RuntimeEvent> history,int nodeBudget,int maxCharacters,out int used) {
    var remaining=Math.Max(1,nodeBudget);var trace=ExplainNode(expression,history,true,ref remaining,maxCharacters);used=Math.Max(1,nodeBudget)-remaining;return trace;
  }
  static TriggerClauseTrace ExplainNode(TriggerExpression x,IReadOnlyList<RuntimeEvent> h,bool isRoot,ref int remaining,int maxCharacters) {
    remaining--;
    if(x==null)return new(){Satisfied=false,Current=0,Required=1};
    var op=(x.Op??"").ToUpperInvariant();
    var children=x.Children??new();var stringsTruncated=false;
    var trace=new TriggerClauseTrace{Op=Trim(op,maxCharacters,ref stringsTruncated),Event=Trim(x.Event,maxCharacters,ref stringsTruncated),Target=Trim(x.Target,maxCharacters,ref stringsTruncated),Satisfied=Eval(x,h),WithinSeconds=isRoot?x.WithinSeconds:null};
    if(op=="EVENT") {
      // Actuals may only come from an event the clause could have considered: same name and,
      // when the clause names a target, the same target. A name-only fallback would harvest
      // all-satisfied where rows from an event the target check already rejected.
      var exact=h.Reverse().FirstOrDefault(v=>EventMatches(x,v));
      var candidate=exact??h.Reverse().FirstOrDefault(v=>string.Equals(x.Event,v.Name,StringComparison.OrdinalIgnoreCase)&&(string.IsNullOrWhiteSpace(x.Target)||string.Equals(x.Target,v.Target,StringComparison.OrdinalIgnoreCase)));
      var where=(x.Where??new()).OrderBy(v=>v.Key,StringComparer.Ordinal).Select(expected=>{
        string actual=null;var found=candidate?.Fields!=null&&candidate.Fields.TryGetValue(expected.Key,out actual);
        return new TriggerWhereTrace{Field=expected.Key,Expected=expected.Value,Actual=found?actual:null,Satisfied=found&&string.Equals(actual,expected.Value,StringComparison.OrdinalIgnoreCase)};
      }).ToArray();
      foreach(var item in where.OrderBy(v=>v.Satisfied?1:0).ThenBy(v=>v.Field,StringComparer.Ordinal).Take(MaxTraceWhereEntries).OrderBy(v=>v.Field,StringComparer.Ordinal)) {
        trace.Where.Add(new(){Field=Trim(item.Field,maxCharacters,ref stringsTruncated),Expected=Trim(item.Expected,maxCharacters,ref stringsTruncated),Actual=Trim(item.Actual,maxCharacters,ref stringsTruncated),Satisfied=item.Satisfied});
      }
      if(where.Length>MaxTraceWhereEntries)trace.Truncated=true;
      trace.Current=trace.Satisfied?1:0;trace.Required=1;if(stringsTruncated)trace.Truncated=true;return trace;
    }
    foreach(var child in children) {
      if(remaining<=0){trace.Truncated=true;break;}
      trace.Children.Add(ExplainNode(child,h,false,ref remaining,maxCharacters));
    }
    if(op=="ANY") {trace.Current=children.Count(v=>Eval(v,h));trace.Required=1;}
    else if(op=="ALL") {trace.Current=children.Count(v=>Eval(v,h));trace.Required=children.Count;}
    else if(op=="COUNT") {
      var required=Math.Max(1,x.Count.GetValueOrDefault());
      var current=children.Count==1?h.Count(v=>EventMatches(children[0],v)):0;
      // Creator-facing progress deliberately saturates at the threshold, matching Measure.
      trace.Current=Math.Min(current,required);trace.Required=required;
    }
    else if(op=="SEQUENCE") {
      int index=0;foreach(var value in h)if(index<children.Count&&EventMatches(children[index],value))index++;
      trace.Current=index;trace.Required=children.Count;trace.SequenceIndex=index;
    }
    else {trace.Current=0;trace.Required=1;}
    if(stringsTruncated)trace.Truncated=true;return trace;
  }
  static int CountNodes(TriggerExpression expression)=>expression==null?1:1+(expression.Children??new()).Sum(CountNodes);
  static int TraceBytes(TriggerClauseTrace trace)=>Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(trace,Formatting.Indented));
  static string Trim(string value,int max,ref bool truncated){if(value==null||value.Length<=max)return value;truncated=true;return value.Substring(0,max);}
  static bool EventMatches(TriggerExpression x,RuntimeEvent v){if(x==null||!string.Equals(x.Op,"EVENT",StringComparison.OrdinalIgnoreCase)||!string.Equals(x.Event,v.Name,StringComparison.OrdinalIgnoreCase))return false;if(!string.IsNullOrWhiteSpace(x.Target)&&!string.Equals(x.Target,v.Target,StringComparison.OrdinalIgnoreCase))return false;foreach(var p in x.Where??new()){if(v.Fields==null||!v.Fields.TryGetValue(p.Key,out var value)||!string.Equals(value,p.Value,StringComparison.OrdinalIgnoreCase))return false;}return true;}
}

public sealed class QuestPackManifest {
  [JsonProperty("schema")] public string Schema { get; set; } = "comfy-quest-pack/v2";
  [JsonProperty("pack_id")] public string PackId { get; set; }
  [JsonProperty("version")] public string Version { get; set; }
  [JsonProperty("content_hash")] public string ContentHash { get; set; }
}

public sealed class PackCandidate { public string Path {get;set;} public QuestPackManifest Manifest {get;set;} public string Sha256 {get;set;} public string ContentHash {get;set;} public IReadOnlyList<ContractDiagnostic> Diagnostics {get;set;} public bool IsValid=>Diagnostics.Count==0; }

public sealed class ActiveSet {
  [JsonProperty("schema")] public string Schema {get;set;}
  [JsonProperty("pack_id")] public string PackId {get;set;}
  [JsonProperty("version")] public string Version {get;set;}
  [JsonProperty("content_hash")] public string ContentHash {get;set;}
  [JsonProperty("package_sha256")] public string PackageSha256 {get;set;}
  [JsonProperty("source")] public string Source {get;set;}
  [JsonProperty("activated_utc")] public DateTimeOffset ActivatedUtc {get;set;}
  [JsonProperty("activation_id", NullValueHandling=NullValueHandling.Ignore)] public string ActivationId {get;set;}
}

public sealed class QuestPackStore {
  public const int MaxArchiveEntries=512;
  public const long MaxExpandedBytes=8L*1024*1024;
  public const int MaxManifestBytes=64*1024;
  readonly string root;
  public QuestPackStore(string rootPath){root=Path.GetFullPath(rootPath??throw new ArgumentNullException(nameof(rootPath)));}
  public IReadOnlyList<PackCandidate> CheckInbox(ISet<string> events=null){var inbox=Path.Combine(root,"inbox");if(!Directory.Exists(inbox))return Array.Empty<PackCandidate>();return Directory.GetFiles(inbox,"*.questpack").Select(x=>Inspect(x,events)).ToArray();}
  public PackCandidate Inspect(string path,ISet<string> events=null){var production=events==null;events??=RuntimeProductionEventCatalog.CreateSet();var errors=new List<ContractDiagnostic>();var full=Path.GetFullPath(path);if(Path.GetDirectoryName(full)!=Path.Combine(root,"inbox"))errors.Add(new("pack.path","$","Pack must be directly inside the runtime inbox."));QuestPackManifest manifest=null;string sha=null,contentHash=null;try{sha=HashFile(full);using var zip=ZipFile.OpenRead(full);if(zip.Entries.Count>MaxArchiveEntries)errors.Add(new("pack.entries","$","Archive exceeds 512 entries."));long expanded=0;foreach(var entry in zip.Entries){if(entry.Length>MaxExpandedBytes-expanded){errors.Add(new("pack.expanded_size","$","Archive expands beyond 8 MiB."));break;}expanded+=entry.Length;if(!SafeEntry(entry.FullName))errors.Add(new("pack.path","$","Unsafe or unsupported archive entry."));}var manifests=zip.Entries.Where(x=>x.FullName=="manifest.json").ToArray();if(manifests.Length!=1)throw new InvalidDataException("Exactly one manifest.json is required");if(manifests[0].Length>MaxManifestBytes)errors.Add(new("pack.manifest_size","$.manifest","Manifest exceeds 64 KiB."));using(var reader=new StreamReader(manifests[0].Open()))manifest=JsonConvert.DeserializeObject<QuestPackManifest>(reader.ReadToEnd());var experiences=zip.Entries.Where(x=>x.FullName.StartsWith("experiences/",StringComparison.Ordinal)&&x.FullName.EndsWith(".json",StringComparison.Ordinal)).OrderBy(x=>x.FullName,StringComparer.Ordinal).ToArray();var contentEntries=new List<KeyValuePair<string,byte[]>>();foreach(var item in experiences){if(item.Length>ExperienceSchema.MaxDocumentBytes){errors.Add(new("document.size",item.FullName,"Experience exceeds 1 MiB."));continue;}using var input=item.Open();using var bytes=new MemoryStream();input.CopyTo(bytes);contentEntries.Add(new(item.FullName,bytes.ToArray()));}contentHash=QuestPackContent.ComputeHash(contentEntries);foreach(var item in contentEntries){var json=System.Text.Encoding.UTF8.GetString(item.Value);errors.AddRange((production?ExperienceCompiler.CompileProductionJson(json):ExperienceCompiler.CompileJson(json,events)).Diagnostics);}if(experiences.Length==0)errors.Add(new("pack.experiences","$","Pack has no experience documents."));if(manifest==null||manifest.Schema!="comfy-quest-pack/v2"||string.IsNullOrWhiteSpace(manifest.PackId)||!SemanticVersion.TryParse(manifest.Version,out _))errors.Add(new("pack.manifest","$.manifest","Pack schema, id, and semantic version are required."));if(manifest!=null&&!string.IsNullOrWhiteSpace(manifest.ContentHash)&&!string.Equals(manifest.ContentHash,contentHash,StringComparison.OrdinalIgnoreCase))errors.Add(new("pack.hash","$.manifest.content_hash","Declared canonical content hash does not match experience documents."));}catch(Exception e){errors.Add(new("pack.read","$",e.Message));}return new(){Path=full,Manifest=manifest,Sha256=sha,ContentHash=contentHash,Diagnostics=errors};}
  static bool SafeEntry(string name){if(string.IsNullOrWhiteSpace(name)||name.Contains("\\")||name.Contains("..")||Path.IsPathRooted(name))return false;if(name=="manifest.json")return true;return (name.StartsWith("experiences/",StringComparison.Ordinal)||name.StartsWith("quests/",StringComparison.Ordinal))&&name.EndsWith(".json",StringComparison.Ordinal)&&name.Count(c=>c=='/')==1;}
  public IReadOnlyList<PackCandidate> ListVersions(ISet<string> events=null)=>CheckInbox(events).Where(x=>x.IsValid).OrderByDescending(x=>SemanticVersion.Parse(x.Manifest.Version)).ThenBy(x=>x.Manifest.PackId,StringComparer.Ordinal).ToArray();
  public PackCandidate LoadLatest(ISet<string> events=null){var valid=ListVersions(events).ToArray();if(valid.Length==0)return null;EnsureNoCollisions(valid);return ActivateCandidate(valid[0]);}
  public PackCandidate LoadVersion(string packId,string version,ISet<string> events=null){var valid=ListVersions(events).ToArray();EnsureNoCollisions(valid);var chosen=valid.SingleOrDefault(x=>string.Equals(x.Manifest.PackId,packId,StringComparison.Ordinal)&&string.Equals(x.Manifest.Version,version,StringComparison.Ordinal));return chosen==null?null:ActivateCandidate(chosen);}
  public PackCandidate Rollback(ISet<string> events=null){var previousPath=Path.Combine(root,"active","active-set.previous.json");if(!File.Exists(previousPath))return null;ActiveSet previous;try{previous=JsonConvert.DeserializeObject<ActiveSet>(File.ReadAllText(previousPath));}catch(Exception e){throw new InvalidOperationException("previous_active_set_unreadable",e);}if(previous==null||previous.Schema!="comfy-quest-active-set/v1"||previous.Source!=Path.GetFileName(previous.Source))throw new InvalidOperationException("previous_active_set_invalid");var candidate=Inspect(Path.Combine(root,"inbox",previous.Source),events);if(!candidate.IsValid||candidate.Manifest.PackId!=previous.PackId||candidate.Manifest.Version!=previous.Version||!string.Equals(candidate.ContentHash,previous.ContentHash,StringComparison.OrdinalIgnoreCase)||!string.Equals(candidate.Sha256,previous.PackageSha256,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("previous_active_content_mismatch");return ActivateCandidate(candidate);}
  void EnsureNoCollisions(IReadOnlyList<PackCandidate> valid){var collision=valid.GroupBy(x=>x.Manifest.PackId+"\n"+x.Manifest.Version,StringComparer.Ordinal).Any(g=>g.Select(x=>x.ContentHash).Distinct(StringComparer.OrdinalIgnoreCase).Count()>1);if(collision)throw new InvalidOperationException("same_version_hash_collision");}
  PackCandidate ActivateCandidate(PackCandidate chosen){var active=Path.Combine(root,"active");Directory.CreateDirectory(active);var activated=DateTimeOffset.UtcNow;var activationId="act-"+activated.ToString("yyyyMMdd'T'HHmmssfff'Z'",CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N").Substring(0,8);var json=JsonConvert.SerializeObject(new ActiveSet{Schema="comfy-quest-active-set/v1",PackId=chosen.Manifest.PackId,Version=chosen.Manifest.Version,ContentHash=chosen.ContentHash,PackageSha256=chosen.Sha256,Source=Path.GetFileName(chosen.Path),ActivatedUtc=activated,ActivationId=activationId},Formatting.Indented);var temp=Path.Combine(active,"active-set.json.tmp");File.WriteAllText(temp,json);var target=Path.Combine(active,"active-set.json");if(File.Exists(target))File.Replace(temp,target,Path.Combine(active,"active-set.previous.json"));else File.Move(temp,target);return chosen;}
  static string HashFile(string p){using var s=File.OpenRead(p);using var h=SHA256.Create();return BitConverter.ToString(h.ComputeHash(s)).Replace("-","").ToLowerInvariant();}
}

public static class QuestPackContent {
  public static string ComputeHash(IEnumerable<KeyValuePair<string,byte[]>> entries){using var content=new MemoryStream();foreach(var item in entries.OrderBy(x=>x.Key,StringComparer.Ordinal)){var name=System.Text.Encoding.UTF8.GetBytes(item.Key+"\n");content.Write(name,0,name.Length);content.Write(item.Value,0,item.Value.Length);}content.Position=0;using var hasher=SHA256.Create();return BitConverter.ToString(hasher.ComputeHash(content)).Replace("-","").ToLowerInvariant();}
}

public readonly struct SemanticVersion : IComparable<SemanticVersion>{public readonly int Major,Minor,Patch;SemanticVersion(int a,int b,int c){Major=a;Minor=b;Patch=c;}public static bool TryParse(string s,out SemanticVersion v){v=default;var p=s?.Split('.');if(p?.Length!=3||!int.TryParse(p[0],System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out var a)||!int.TryParse(p[1],System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out var b)||!int.TryParse(p[2],System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out var c)||a<0||b<0||c<0||!string.Equals(s,$"{a}.{b}.{c}",StringComparison.Ordinal))return false;v=new(a,b,c);return true;}public static SemanticVersion Parse(string s)=>TryParse(s,out var v)?v:throw new FormatException("Invalid semantic version.");public int CompareTo(SemanticVersion o){var x=Major.CompareTo(o.Major);if(x!=0)return x;x=Minor.CompareTo(o.Minor);return x!=0?x:Patch.CompareTo(o.Patch);}}
