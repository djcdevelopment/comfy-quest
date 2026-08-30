namespace ComfyQuestContracts;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>The standalone file boundary shared with spatial authoring tools. These files are
/// authoring inputs, not questpacks and not Runtime activation requests.</summary>
public static class SpatialExchangeSchema {
  public const string Anchor = "comfy-quest-spatial-anchor/v1";
  public const string Evidence = "comfy-quest-spatial-evidence/v1";
  public const int MaxDocumentBytes = 256 * 1024;
}

public sealed class SpatialAnchorExchange {
  [JsonProperty("schema")] public string Schema { get; set; } = SpatialExchangeSchema.Anchor;
  [JsonProperty("anchor_id")] public string AnchorId { get; set; }
  [JsonProperty("mode")] public string Mode { get; set; }
  [JsonProperty("shape")] public string Shape { get; set; } = "sphere";
  [JsonProperty("radius_meters")] public double RadiusMeters { get; set; }
  [JsonProperty("snapshot")] public SpatialSnapshotReference Snapshot { get; set; }
  [JsonProperty("piece")] public SpatialPieceReference Piece { get; set; }
  [JsonProperty("producer")] public SpatialProducerReference Producer { get; set; }
  [JsonProperty("content_sha256")] public string ContentSha256 { get; set; }
}

public sealed class SpatialSnapshotReference {
  [JsonProperty("snapshot_id")] public long SnapshotId { get; set; }
  [JsonProperty("world_id")] public string WorldId { get; set; }
  [JsonProperty("file_sha256")] public string FileSha256 { get; set; }
}

public sealed class SpatialPieceReference {
  [JsonProperty("zdo_index")] public int ZdoIndex { get; set; }
  [JsonProperty("prefab")] public string Prefab { get; set; }
  [JsonProperty("position")] public SpatialContractPoint Position { get; set; }
}

public sealed class SpatialProducerReference {
  [JsonProperty("repository")] public string Repository { get; set; }
  [JsonProperty("revision")] public string Revision { get; set; }
}

public sealed class SpatialContractPoint {
  public SpatialContractPoint() { }
  public SpatialContractPoint(double x,double y,double z){X=x;Y=y;Z=z;}
  [JsonProperty("x")] public double X { get; set; }
  [JsonProperty("y")] public double Y { get; set; }
  [JsonProperty("z")] public double Z { get; set; }
}

public sealed class SpatialEvidenceBundle {
  [JsonProperty("schema")] public string Schema { get; set; } = SpatialExchangeSchema.Evidence;
  [JsonProperty("exported_utc")] public DateTimeOffset ExportedUtc { get; set; }
  [JsonProperty("project_id")] public string ProjectId { get; set; }
  [JsonProperty("experience_id")] public string ExperienceId { get; set; }
  [JsonProperty("pack_id")] public string PackId { get; set; }
  [JsonProperty("content_hash")] public string ContentHash { get; set; }
  [JsonProperty("activation_id")] public string ActivationId { get; set; }
  [JsonProperty("run_id")] public string RunId { get; set; }
  [JsonProperty("world_uid")] public string WorldUid { get; set; }
  [JsonProperty("records")] public List<SpatialEvidenceRecord> Records { get; set; } = new();
  [JsonProperty("content_sha256")] public string ContentSha256 { get; set; }
}

public sealed class SpatialEvidenceRecord {
  [JsonProperty("receipt_id")] public string ReceiptId { get; set; }
  [JsonProperty("at_utc")] public DateTimeOffset AtUtc { get; set; }
  [JsonProperty("correlation_id")] public string CorrelationId { get; set; }
  [JsonProperty("transition_id")] public string TransitionId { get; set; }
  [JsonProperty("event_name")] public string EventName { get; set; }
  [JsonProperty("area_id")] public string AreaId { get; set; }
  [JsonProperty("predicate")] public string Predicate { get; set; }
  [JsonProperty("current_count")] public int CurrentCount { get; set; }
  [JsonProperty("required_count")] public int RequiredCount { get; set; }
  [JsonProperty("anchor_sha256")] public string AnchorSha256 { get; set; }
  [JsonProperty("snapshot")] public SpatialSnapshotReference Snapshot { get; set; }
  [JsonProperty("piece")] public SpatialPieceReference Piece { get; set; }
  [JsonProperty("resolved_center")] public SpatialContractPoint ResolvedCenter { get; set; }
  [JsonProperty("radius_meters")] public double RadiusMeters { get; set; }
  [JsonProperty("observed_position")] public SpatialContractPoint ObservedPosition { get; set; }
  [JsonProperty("distance_meters")] public double? DistanceMeters { get; set; }
  [JsonProperty("satisfied")] public bool Satisfied { get; set; }
}

public static class SpatialExchangeContract {
  static readonly JsonSerializerSettings Strict = new() {
    MissingMemberHandling = MissingMemberHandling.Error,
    DateParseHandling = DateParseHandling.DateTimeOffset,
  };

  public static SpatialAnchorExchange ParseAnchor(string json) {
    if(string.IsNullOrWhiteSpace(json)||Encoding.UTF8.GetByteCount(json)>SpatialExchangeSchema.MaxDocumentBytes)
      throw new SpatialContractException("anchor_document_size");
    SpatialAnchorExchange value;
    try {
      using var input=new StringReader(json);
      using var reader=new JsonTextReader(input){DateParseHandling=DateParseHandling.None};
      var token=JToken.ReadFrom(reader,new JsonLoadSettings{
        CommentHandling=CommentHandling.Load,
        DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error,
        LineInfoHandling=LineInfoHandling.Load,
      });
      if(token.Type==JTokenType.Comment
          ||(token is JContainer container&&container.Descendants().Any(item=>item.Type==JTokenType.Comment))
          ||reader.Read())
        throw new JsonReaderException("Comments and trailing JSON values are not allowed.");
      value=token.ToObject<SpatialAnchorExchange>(JsonSerializer.Create(Strict));
    }
    catch(JsonException e){throw new SpatialContractException("anchor_json_invalid",e);}
    ValidateAnchor(value,true);
    return value;
  }

  public static void ValidateAnchor(SpatialAnchorExchange value,bool verifyHash) {
    if(value==null)throw new SpatialContractException("anchor_required");
    if(value.Schema!=SpatialExchangeSchema.Anchor)throw new SpatialContractException("anchor_schema_unsupported");
    if(!Stable(value.AnchorId))throw new SpatialContractException("anchor_id_invalid");
    if(value.Mode!="world"&&value.Mode!="binding_relative")throw new SpatialContractException("anchor_mode_invalid");
    if(value.Shape!="sphere")throw new SpatialContractException("anchor_shape_invalid");
    if(!Finite(value.RadiusMeters)||value.RadiusMeters<SpatialPredicateCatalog.RadiusMinimum||value.RadiusMeters>SpatialPredicateCatalog.RadiusMaximum)
      throw new SpatialContractException("anchor_radius_invalid");
    if(value.Snapshot==null||value.Snapshot.SnapshotId<=0||!BoundedText(value.Snapshot.WorldId,128)||!Sha(value.Snapshot.FileSha256))
      throw new SpatialContractException("anchor_snapshot_invalid");
    if(value.Piece==null||value.Piece.ZdoIndex<0||!BoundedText(value.Piece.Prefab,256)||value.Piece.Position==null
        ||!Coordinate(value.Piece.Position.X)||!Coordinate(value.Piece.Position.Y)||!Coordinate(value.Piece.Position.Z))
      throw new SpatialContractException("anchor_piece_invalid");
    if(value.Producer==null||value.Producer.Repository!="ComfyStewardView"||!Hex(value.Producer.Revision,40))
      throw new SpatialContractException("anchor_producer_invalid");
    if(verifyHash&&(!Sha(value.ContentSha256)||!string.Equals(value.ContentSha256,ComputeAnchorHash(value),StringComparison.OrdinalIgnoreCase)))
      throw new SpatialContractException("anchor_hash_mismatch");
  }

  public static string ComputeAnchorHash(SpatialAnchorExchange value) {
    if(value==null)throw new ArgumentNullException(nameof(value));
    var parts=new[]{value.Schema,value.AnchorId,value.Mode,value.Shape,Number(value.RadiusMeters),
      value.Snapshot?.SnapshotId.ToString(CultureInfo.InvariantCulture),value.Snapshot?.WorldId,value.Snapshot?.FileSha256?.ToLowerInvariant(),
      value.Piece?.ZdoIndex.ToString(CultureInfo.InvariantCulture),value.Piece?.Prefab,Number(value.Piece?.Position?.X),Number(value.Piece?.Position?.Y),Number(value.Piece?.Position?.Z),
      value.Producer?.Repository,value.Producer?.Revision?.ToLowerInvariant()};
    return Hash(string.Join("\n",parts.Select(part=>part??string.Empty)));
  }

  public static string ComputeEvidenceHash(SpatialEvidenceBundle value) {
    if(value==null)throw new ArgumentNullException(nameof(value));
    var lines=new List<string>{value.Schema,value.ExportedUtc.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture),value.ProjectId,value.ExperienceId,value.PackId,value.ContentHash?.ToLowerInvariant(),value.ActivationId,value.RunId,value.WorldUid};
    foreach(var item in value.Records??new())lines.AddRange(new[]{item.ReceiptId,item.AtUtc.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture),item.CorrelationId,item.TransitionId,item.EventName,item.AreaId,item.Predicate,item.CurrentCount.ToString(CultureInfo.InvariantCulture),item.RequiredCount.ToString(CultureInfo.InvariantCulture),item.AnchorSha256?.ToLowerInvariant(),
      item.Snapshot?.SnapshotId.ToString(CultureInfo.InvariantCulture),item.Snapshot?.WorldId,item.Snapshot?.FileSha256?.ToLowerInvariant(),
      item.Piece?.ZdoIndex.ToString(CultureInfo.InvariantCulture),item.Piece?.Prefab,Number(item.Piece?.Position?.X),Number(item.Piece?.Position?.Y),Number(item.Piece?.Position?.Z),
      Number(item.ResolvedCenter?.X),Number(item.ResolvedCenter?.Y),Number(item.ResolvedCenter?.Z),Number(item.RadiusMeters),Number(item.ObservedPosition?.X),Number(item.ObservedPosition?.Y),Number(item.ObservedPosition?.Z),Number(item.DistanceMeters),item.Satisfied?"true":"false"});
    return Hash(string.Join("\n",lines.Select(line=>line??string.Empty)));
  }

  public static void ValidateEvidence(SpatialEvidenceBundle value,bool verifyHash) {
    if(value==null||value.Schema!=SpatialExchangeSchema.Evidence||value.ExportedUtc==default||!BoundedText(value.ProjectId,80)||!Stable(value.ExperienceId)||!Stable(value.PackId)
        ||!Sha(value.ContentHash)||!BoundedText(value.ActivationId,128)||!BoundedText(value.RunId,128)||!BoundedText(value.WorldUid,64)
        ||value.Records==null||value.Records.Count==0||value.Records.Count>512)throw new SpatialContractException("evidence_envelope_invalid");
    foreach(var item in value.Records){
      if(item==null||item.AtUtc==default||!BoundedText(item.ReceiptId,160)||!OptionalText(item.CorrelationId,160)||!OptionalStable(item.TransitionId)
          ||!OptionalText(item.EventName,128)||!Stable(item.AreaId)||!SpatialPredicateCatalog.TryGet(item.Predicate,out _)||!Sha(item.AnchorSha256)
          ||item.CurrentCount<0||item.RequiredCount<1||item.CurrentCount>item.RequiredCount
          ||item.Satisfied!=(item.CurrentCount>=item.RequiredCount)
          ||item.Snapshot==null||item.Snapshot.SnapshotId<=0||!BoundedText(item.Snapshot.WorldId,128)||!Sha(item.Snapshot.FileSha256)
          ||item.Piece==null||item.Piece.ZdoIndex<0||!BoundedText(item.Piece.Prefab,256)||item.Piece.Position==null
          ||!Coordinate(item.Piece.Position.X)||!Coordinate(item.Piece.Position.Y)||!Coordinate(item.Piece.Position.Z)
          ||item.ResolvedCenter==null
          ||!Coordinate(item.ResolvedCenter.X)||!Coordinate(item.ResolvedCenter.Y)||!Coordinate(item.ResolvedCenter.Z)
          ||!Finite(item.RadiusMeters)||item.RadiusMeters<SpatialPredicateCatalog.RadiusMinimum||item.RadiusMeters>SpatialPredicateCatalog.RadiusMaximum)
        throw new SpatialContractException("evidence_record_invalid");
      var counted=string.Equals(item.Predicate,"count_in_area",StringComparison.Ordinal);
      if(counted) {
        if(item.ObservedPosition!=null||item.DistanceMeters.HasValue)
          throw new SpatialContractException("evidence_record_invalid");
      } else if(item.ObservedPosition==null||!Coordinate(item.ObservedPosition.X)||!Coordinate(item.ObservedPosition.Y)||!Coordinate(item.ObservedPosition.Z)
          ||!item.DistanceMeters.HasValue||!Finite(item.DistanceMeters.Value)||item.DistanceMeters.Value<0
          ||Math.Abs(item.DistanceMeters.Value-Distance(item.ResolvedCenter,item.ObservedPosition))>0.000001)
        throw new SpatialContractException("evidence_record_invalid");
    }
    if(verifyHash&&(!Sha(value.ContentSha256)||!string.Equals(value.ContentSha256,ComputeEvidenceHash(value),StringComparison.OrdinalIgnoreCase)))throw new SpatialContractException("evidence_hash_mismatch");
  }

  static bool Stable(string value)=>!string.IsNullOrWhiteSpace(value)&&value.Length<=64&&value.All(ch=>char.IsLetterOrDigit(ch)||ch is '-' or '_' or '$');
  static bool BoundedText(string value,int max)=>!string.IsNullOrWhiteSpace(value)&&value.Length<=max&&!value.Any(char.IsControl);
  static bool OptionalText(string value,int max)=>value==null||value.Length<=max&&!value.Any(char.IsControl);
  static bool OptionalStable(string value)=>value==null||Stable(value);
  static bool Hex(string value,int length)=>value!=null&&value.Length==length&&value.All(Uri.IsHexDigit);
  static bool Sha(string value)=>Hex(value,64);
  static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
  static bool Coordinate(double value)=>Finite(value)&&Math.Abs(value)<=SpatialPredicateCatalog.MaxWorldCoordinate;
  static double Distance(SpatialContractPoint left,SpatialContractPoint right){var x=left.X-right.X;var y=left.Y-right.Y;var z=left.Z-right.Z;return Math.Sqrt(x*x+y*y+z*z);}
  static string Number(double? value)=>value.HasValue?Number(value.Value):string.Empty;
  // Hash numeric scalars as normalized IEEE-754 binary64 bits. Decimal shortest-string rules
  // differ between .NET and Java around exponent thresholds, so textual round-trip formats are
  // not a cross-runtime canonicalization. Signed zeros are the same spatial coordinate here.
  static string Number(double value){
    if(!Finite(value))throw new SpatialContractException("number_invalid");
    if(value==0d)return "0000000000000000";
    return unchecked((ulong)BitConverter.DoubleToInt64Bits(value)).ToString("x16",CultureInfo.InvariantCulture);
  }
  static string Hash(string value){using var sha=SHA256.Create();return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(value)).Select(b=>b.ToString("x2",CultureInfo.InvariantCulture)));}
}

public sealed class SpatialContractException : Exception {
  public SpatialContractException(string code):base(code){Code=code;}
  public SpatialContractException(string code,Exception inner):base(code,inner){Code=code;}
  public string Code { get; }
}
