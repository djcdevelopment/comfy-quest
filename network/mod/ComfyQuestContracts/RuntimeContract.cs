namespace ComfyQuestContracts;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;

public sealed class RuntimeEvent {
  public string Name { get; set; }
  public string Target { get; set; }
  public string SourceId { get; set; }
  public DateTimeOffset At { get; set; }
  public IReadOnlyDictionary<string,string> Fields { get; set; }
}

public static class TriggerEvaluator {
  public static bool Matches(TriggerExpression expression, IReadOnlyList<RuntimeEvent> history) {
    history ??= Array.Empty<RuntimeEvent>();
    var bounded = expression.WithinSeconds.HasValue && history.Count > 0
      ? history.Where(x => x.At >= history[history.Count-1].At.AddSeconds(-expression.WithinSeconds.Value)).ToArray()
      : history.ToArray();
    return Eval(expression, bounded);
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
}

public sealed class QuestPackStore {
  public const int MaxArchiveEntries=512;
  public const long MaxExpandedBytes=8L*1024*1024;
  public const int MaxManifestBytes=64*1024;
  readonly string root;
  public QuestPackStore(string rootPath){root=Path.GetFullPath(rootPath??throw new ArgumentNullException(nameof(rootPath)));}
  public IReadOnlyList<PackCandidate> CheckInbox(ISet<string> events=null){events??=CanonicalEventCatalog.CreateSet();var inbox=Path.Combine(root,"inbox");if(!Directory.Exists(inbox))return Array.Empty<PackCandidate>();return Directory.GetFiles(inbox,"*.questpack").Select(x=>Inspect(x,events)).ToArray();}
  public PackCandidate Inspect(string path,ISet<string> events=null){events??=CanonicalEventCatalog.CreateSet();var errors=new List<ContractDiagnostic>();var full=Path.GetFullPath(path);if(Path.GetDirectoryName(full)!=Path.Combine(root,"inbox"))errors.Add(new("pack.path","$","Pack must be directly inside the runtime inbox."));QuestPackManifest manifest=null;string sha=null,contentHash=null;try{sha=HashFile(full);using var zip=ZipFile.OpenRead(full);if(zip.Entries.Count>MaxArchiveEntries)errors.Add(new("pack.entries","$","Archive exceeds 512 entries."));long expanded=0;foreach(var entry in zip.Entries){if(entry.Length>MaxExpandedBytes-expanded){errors.Add(new("pack.expanded_size","$","Archive expands beyond 8 MiB."));break;}expanded+=entry.Length;if(!SafeEntry(entry.FullName))errors.Add(new("pack.path","$","Unsafe or unsupported archive entry."));}var manifests=zip.Entries.Where(x=>x.FullName=="manifest.json").ToArray();if(manifests.Length!=1)throw new InvalidDataException("Exactly one manifest.json is required");if(manifests[0].Length>MaxManifestBytes)errors.Add(new("pack.manifest_size","$.manifest","Manifest exceeds 64 KiB."));using(var reader=new StreamReader(manifests[0].Open()))manifest=JsonConvert.DeserializeObject<QuestPackManifest>(reader.ReadToEnd());var experiences=zip.Entries.Where(x=>x.FullName.StartsWith("experiences/",StringComparison.Ordinal)&&x.FullName.EndsWith(".json",StringComparison.Ordinal)).OrderBy(x=>x.FullName,StringComparer.Ordinal).ToArray();var contentEntries=new List<KeyValuePair<string,byte[]>>();foreach(var item in experiences){if(item.Length>ExperienceSchema.MaxDocumentBytes){errors.Add(new("document.size",item.FullName,"Experience exceeds 1 MiB."));continue;}using var input=item.Open();using var bytes=new MemoryStream();input.CopyTo(bytes);contentEntries.Add(new(item.FullName,bytes.ToArray()));}contentHash=QuestPackContent.ComputeHash(contentEntries);foreach(var item in contentEntries)errors.AddRange(ExperienceCompiler.CompileJson(System.Text.Encoding.UTF8.GetString(item.Value),events).Diagnostics);if(experiences.Length==0)errors.Add(new("pack.experiences","$","Pack has no experience documents."));if(manifest==null||manifest.Schema!="comfy-quest-pack/v2"||string.IsNullOrWhiteSpace(manifest.PackId)||!SemanticVersion.TryParse(manifest.Version,out _))errors.Add(new("pack.manifest","$.manifest","Pack schema, id, and semantic version are required."));if(manifest!=null&&!string.IsNullOrWhiteSpace(manifest.ContentHash)&&!string.Equals(manifest.ContentHash,contentHash,StringComparison.OrdinalIgnoreCase))errors.Add(new("pack.hash","$.manifest.content_hash","Declared canonical content hash does not match experience documents."));}catch(Exception e){errors.Add(new("pack.read","$",e.Message));}return new(){Path=full,Manifest=manifest,Sha256=sha,ContentHash=contentHash,Diagnostics=errors};}
  static bool SafeEntry(string name){if(string.IsNullOrWhiteSpace(name)||name.Contains("\\")||name.Contains("..")||Path.IsPathRooted(name))return false;if(name=="manifest.json")return true;return (name.StartsWith("experiences/",StringComparison.Ordinal)||name.StartsWith("quests/",StringComparison.Ordinal))&&name.EndsWith(".json",StringComparison.Ordinal)&&name.Count(c=>c=='/')==1;}
  public IReadOnlyList<PackCandidate> ListVersions(ISet<string> events=null)=>CheckInbox(events).Where(x=>x.IsValid).OrderByDescending(x=>SemanticVersion.Parse(x.Manifest.Version)).ThenBy(x=>x.Manifest.PackId,StringComparer.Ordinal).ToArray();
  public PackCandidate LoadLatest(ISet<string> events=null){var valid=ListVersions(events).ToArray();if(valid.Length==0)return null;EnsureNoCollisions(valid);return ActivateCandidate(valid[0]);}
  public PackCandidate LoadVersion(string packId,string version,ISet<string> events=null){var valid=ListVersions(events).ToArray();EnsureNoCollisions(valid);var chosen=valid.SingleOrDefault(x=>string.Equals(x.Manifest.PackId,packId,StringComparison.Ordinal)&&string.Equals(x.Manifest.Version,version,StringComparison.Ordinal));return chosen==null?null:ActivateCandidate(chosen);}
  public PackCandidate Rollback(ISet<string> events=null){var previousPath=Path.Combine(root,"active","active-set.previous.json");if(!File.Exists(previousPath))return null;ActiveSet previous;try{previous=JsonConvert.DeserializeObject<ActiveSet>(File.ReadAllText(previousPath));}catch(Exception e){throw new InvalidOperationException("previous_active_set_unreadable",e);}if(previous==null||previous.Schema!="comfy-quest-active-set/v1"||previous.Source!=Path.GetFileName(previous.Source))throw new InvalidOperationException("previous_active_set_invalid");var candidate=Inspect(Path.Combine(root,"inbox",previous.Source),events);if(!candidate.IsValid||candidate.Manifest.PackId!=previous.PackId||candidate.Manifest.Version!=previous.Version||!string.Equals(candidate.ContentHash,previous.ContentHash,StringComparison.OrdinalIgnoreCase)||!string.Equals(candidate.Sha256,previous.PackageSha256,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("previous_active_content_mismatch");return ActivateCandidate(candidate);}
  void EnsureNoCollisions(IReadOnlyList<PackCandidate> valid){var collision=valid.GroupBy(x=>x.Manifest.PackId+"\n"+x.Manifest.Version,StringComparer.Ordinal).Any(g=>g.Select(x=>x.ContentHash).Distinct(StringComparer.OrdinalIgnoreCase).Count()>1);if(collision)throw new InvalidOperationException("same_version_hash_collision");}
  PackCandidate ActivateCandidate(PackCandidate chosen){var active=Path.Combine(root,"active");Directory.CreateDirectory(active);var json=JsonConvert.SerializeObject(new ActiveSet{Schema="comfy-quest-active-set/v1",PackId=chosen.Manifest.PackId,Version=chosen.Manifest.Version,ContentHash=chosen.ContentHash,PackageSha256=chosen.Sha256,Source=Path.GetFileName(chosen.Path),ActivatedUtc=DateTimeOffset.UtcNow},Formatting.Indented);var temp=Path.Combine(active,"active-set.json.tmp");File.WriteAllText(temp,json);var target=Path.Combine(active,"active-set.json");if(File.Exists(target))File.Replace(temp,target,Path.Combine(active,"active-set.previous.json"));else File.Move(temp,target);return chosen;}
  static string HashFile(string p){using var s=File.OpenRead(p);using var h=SHA256.Create();return BitConverter.ToString(h.ComputeHash(s)).Replace("-","").ToLowerInvariant();}
}

public static class QuestPackContent {
  public static string ComputeHash(IEnumerable<KeyValuePair<string,byte[]>> entries){using var content=new MemoryStream();foreach(var item in entries.OrderBy(x=>x.Key,StringComparer.Ordinal)){var name=System.Text.Encoding.UTF8.GetBytes(item.Key+"\n");content.Write(name,0,name.Length);content.Write(item.Value,0,item.Value.Length);}content.Position=0;using var hasher=SHA256.Create();return BitConverter.ToString(hasher.ComputeHash(content)).Replace("-","").ToLowerInvariant();}
}

public readonly struct SemanticVersion : IComparable<SemanticVersion>{public readonly int Major,Minor,Patch;SemanticVersion(int a,int b,int c){Major=a;Minor=b;Patch=c;}public static bool TryParse(string s,out SemanticVersion v){v=default;var p=s?.Split('.');if(p?.Length!=3||!int.TryParse(p[0],out var a)||!int.TryParse(p[1],out var b)||!int.TryParse(p[2],out var c)||a<0||b<0||c<0)return false;v=new(a,b,c);return true;}public static SemanticVersion Parse(string s)=>TryParse(s,out var v)?v:throw new FormatException("Invalid semantic version.");public int CompareTo(SemanticVersion o){var x=Major.CompareTo(o.Major);if(x!=0)return x;x=Minor.CompareTo(o.Minor);return x!=0?x:Patch.CompareTo(o.Patch);}}
