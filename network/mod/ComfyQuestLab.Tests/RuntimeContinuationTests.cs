namespace ComfyNetworkSense.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using ComfyQuestContracts;
using Newtonsoft.Json;
using Xunit;

public sealed class RuntimeContinuationTests {
  static readonly HashSet<string> Events=new(){"kill"};
  const string Hash="0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

  [Fact] public void SuccessorsAreBoundedStableUniqueAndNotSelf() {
    Assert.True(ExperienceCompiler.CompileJson(Document("alpha",new[]{"beta"}),Events).IsValid);
    Assert.Contains(ExperienceCompiler.CompileJson(Document("alpha",new[]{"beta","beta"}),Events).Diagnostics,
      value=>value.Code=="successor.invalid");
    Assert.Contains(ExperienceCompiler.CompileJson(Document("alpha",new[]{"alpha"}),Events).Diagnostics,
      value=>value.Code=="successor.self");
    Assert.Contains(ExperienceCompiler.CompileJson(Document("alpha",
      Enumerable.Range(0,ExperienceSchema.MaxSuccessors+1).Select(index=>"next-"+index).ToArray()),Events).Diagnostics,
      value=>value.Code=="successors.bounds");
  }

  [Fact] public void PackInspectionRejectsMissingAndCyclicSuccessors() {
    Run(root=>{
      var missing=Path.Combine(root,"inbox","missing.questpack");
      WritePack(missing,("alpha",Document("alpha",new[]{"absent"})));
      Assert.Contains(new QuestPackStore(root).Inspect(missing,Events).Diagnostics,
        value=>value.Code=="successor.missing");

      var cyclic=Path.Combine(root,"inbox","cyclic.questpack");
      WritePack(cyclic,("alpha",Document("alpha",new[]{"beta"})),
        ("beta",Document("beta",new[]{"alpha"})));
      Assert.Contains(new QuestPackStore(root).Inspect(cyclic,Events).Diagnostics,
        value=>value.Code=="successor.cycle");

      var linear=Path.Combine(root,"inbox","linear.questpack");
      WritePack(linear,("alpha",Document("alpha",new[]{"beta"})),("beta",Document("beta")));
      Assert.True(new QuestPackStore(root).Inspect(linear,Events).IsValid);
    });
  }

  [Fact] public void AuthoredOrderUsesExactEligibilityAndSkipsCompletedSuccessors() {
    var source=Scope("alpha");
    var documents=new Dictionary<string,ExperienceDocument>(StringComparer.Ordinal) {
      ["alpha"]=JsonConvert.DeserializeObject<ExperienceDocument>(Document("alpha",new[]{"beta","gamma"})),
      ["beta"]=JsonConvert.DeserializeObject<ExperienceDocument>(Document("beta",prerequisites:new[]{"gate"})),
      ["gamma"]=JsonConvert.DeserializeObject<ExperienceDocument>(Document("gamma")),
      ["gate"]=JsonConvert.DeserializeObject<ExperienceDocument>(Document("gate")),
    };
    var history=new List<RuntimeRunRecord>{Complete("source",source),
      Complete("wrong-participants",Scope("gate",participants:new[]{"1","3"}))};
    Assert.Equal("gamma",ExperienceContinuationSelector.FirstEligible(documents["alpha"],documents,history,source));

    history.Add(Complete("gate",Scope("gate")));
    Assert.Equal("beta",ExperienceContinuationSelector.FirstEligible(documents["alpha"],documents,history,source));
    history.Add(Complete("beta",Scope("beta")));
    Assert.Equal("gamma",ExperienceContinuationSelector.FirstEligible(documents["alpha"],documents,history,source));
    history.Add(Complete("gamma",Scope("gamma")));
    Assert.Null(ExperienceContinuationSelector.FirstEligible(documents["alpha"],documents,history,source));
  }

  [Fact] public void DurableDecisionPinsOneSuccessorAcrossRetry() {
    Run(root=>{
      var source=Complete("run-source",Scope("alpha"));
      var store=new RuntimeContinuationStore(root);
      var first=store.Begin(source,"beta","guild","1.0.0","hash","act-20260829T120000000Z-deadbeef",DateTimeOffset.UnixEpoch);
      var retry=new RuntimeContinuationStore(root).Begin(source,"beta","guild","1.0.0","hash","act-20260829T120000000Z-deadbeef",DateTimeOffset.UnixEpoch.AddMinutes(1));
      Assert.Equal(first.HandoffId,retry.HandoffId);
      Assert.Equal("continuation_decision_conflict",Assert.Throws<InvalidOperationException>(()=>
        store.Begin(source,"gamma","guild","1.0.0","hash","act-20260829T120000000Z-deadbeef",DateTimeOffset.UnixEpoch)).Message);
      var failed=Complete("run-failed",Scope("failed"));failed.Outcome="fail";
      Assert.Equal("continuation_scope_invalid",Assert.Throws<ArgumentException>(()=>
        store.Begin(failed,"beta","guild","1.0.0","hash","act-20260829T120000000Z-deadbeef",DateTimeOffset.UnixEpoch)).Message);
      Assert.Single(store.Pending());
      store.SetSuccessorRun(first.HandoffId,"run-beta");
      store.Complete(first.HandoffId,DateTimeOffset.UnixEpoch.AddSeconds(5));
      Assert.Empty(new RuntimeContinuationStore(root).Pending());
      Assert.Equal("completed",new RuntimeContinuationStore(root).Find(first.HandoffId).State);
    });
  }

  [Fact] public void RunContinuationIsIdempotentAndPreservesExactCampaignScope() {
    Run(root=>{
      var registry=new RuntimeRunRegistry(root);
      var source=registry.Resolve(Scope("alpha"),false,DateTimeOffset.UnixEpoch);
      registry.MarkOutcome(source.RunId,"complete",DateTimeOffset.UnixEpoch.AddSeconds(1));
      var first=registry.StartContinuation(source.RunId,"handoff-one","beta","10:20",DateTimeOffset.UnixEpoch.AddSeconds(2));
      var retry=new RuntimeRunRegistry(root).StartContinuation(source.RunId,"handoff-one","beta","10:20",DateTimeOffset.UnixEpoch.AddMinutes(1));
      Assert.Equal(first.RunId,retry.RunId);
      Assert.Equal(2,registry.List().Count);
      Assert.Equal("alpha",registry.Find(source.RunId).Scope.ExperienceId);
      Assert.Equal("beta",first.Scope.ExperienceId);
      Assert.Equal(source.Scope.WorldId,first.Scope.WorldId);
      Assert.Equal(source.Scope.BindingZdo,first.Scope.BindingZdo);
      Assert.Equal(source.Scope.BindingInstanceId,first.Scope.BindingInstanceId);
      Assert.Equal(source.Scope.ContentHash,first.Scope.ContentHash);
      Assert.Equal(new[]{"1","2"},first.Scope.ParticipantIds);
      Assert.Equal(source.RunId,first.ContinuationPredecessorRunId);
      Assert.Equal("handoff-one",first.ContinuationId);
    });
  }

  [Fact] public void ContinuationBindingKeepsTheDurableMarkerAndRecoversIdempotently() {
    Run(root=>{
      var prior=new RuntimeBindingReference{PackId="guild",ExperienceId="alpha",BindingId="default",
        Version="1.0.0",ContentHash=Hash,BindingInstanceId="binding-20260829T120000000Z-deadbeef"};
      var adapter=new BindingAdapter(prior);
      var coordinator=new RuntimeBindingCoordinator(root,adapter);
      var active=new ActiveSet{PackId="guild",Version="1.0.0",ContentHash=Hash,ExperienceId="beta"};
      var successor=JsonConvert.DeserializeObject<ExperienceDocument>(Document("beta"));
      var first=coordinator.Continue("10:20","42",active,"alpha",successor,
        prior.BindingInstanceId,"sign",DateTimeOffset.UnixEpoch);
      var retry=coordinator.Continue("10:20","42",active,"alpha",successor,
        prior.BindingInstanceId,"sign",DateTimeOffset.UnixEpoch.AddSeconds(1));
      Assert.Equal(first.ChangeId,retry.ChangeId);
      Assert.Equal("beta",adapter.Value.ExperienceId);
      Assert.Equal(prior.BindingInstanceId,adapter.Value.BindingInstanceId);
      Assert.Single(Directory.GetFiles(Path.Combine(root,"state","binding-changes"),"*.json"));
    });
  }

  [Fact] public void PendingAndStartedReceiptsAreWriteOnce() {
    Run(root=>{
      var store=new RuntimeReceiptStore(root);
      var id="handoff-proof-pending";
      var first=store.WriteOnce(new RuntimeReceipt{Id=id,Operation="continuation",Status="pending"});
      var retry=store.WriteOnce(new RuntimeReceipt{Id=id,Operation="continuation",Status="wrong"});
      Assert.Equal(first,retry);
      Assert.Single(store.List());
      Assert.True(store.ContainsId(id));
      Assert.Equal("pending",JsonConvert.DeserializeObject<RuntimeReceipt>(File.ReadAllText(first)).Status);
    });
  }

  [Fact] public void ContinuationJournalBoundsAreDeclaredAndExecutable() {
    Assert.Equal(512,RuntimeContinuationStore.MaxRecords);
    Assert.Equal(4*1024*1024,RuntimeContinuationStore.MaxFileBytes);
    Run(root=>{
      var state=Directory.CreateDirectory(Path.Combine(root,"state")).FullName;
      var path=Path.Combine(state,"continuations.json");
      File.WriteAllText(path,JsonConvert.SerializeObject(new {schema="comfy-quest-runtime-continuations/v1",
        records=Enumerable.Range(0,RuntimeContinuationStore.MaxRecords+1).Select(_=>new{}).ToArray()}));
      Assert.Equal("continuation_store_unreadable",Assert.Throws<InvalidDataException>(()=>
        new RuntimeContinuationStore(root).Pending()).Message);
      File.WriteAllText(path,new string('x',RuntimeContinuationStore.MaxFileBytes+1));
      Assert.Equal("continuation_store_unreadable",Assert.Throws<InvalidDataException>(()=>
        new RuntimeContinuationStore(root).Pending()).Message);
    });
  }

  static RuntimeRunScope Scope(string experience,string[] participants=null)=>new(){WorldId="42",
    ExperienceId=experience,BindingZdo="10:20",BindingInstanceId="binding-20260829T120000000Z-deadbeef",
    ContentHash="hash",ParticipantIds=(participants??new[]{"2","1"}).ToList()};
  static RuntimeRunRecord Complete(string id,RuntimeRunScope scope)=>new(){RunId=id,ScopeId=scope.ScopeId,
    Scope=scope,StateKey=id,Status="active",StartedUtc=DateTimeOffset.UnixEpoch,Outcome="complete"};

  static string Document(string id,string[] successors=null,string[] prerequisites=null)=>JsonConvert.SerializeObject(new ExperienceDocument{
    Schema=ExperienceSchema.Id,Id=id,EntryStage="start",SuccessorExperienceIds=successors?.ToList(),
    Prerequisites=prerequisites?.ToList(),Stages=new(){new ExperienceStage{Id="start",Transitions=new(){
      new ExperienceTransition{Id="done",Priority=1,When=new TriggerExpression{Op="EVENT",Event="kill"},Outcome="complete"}}}},
    Bindings=new(){new ExperienceBinding{Id="default",ExperienceId=id,TargetKinds=new(){"sign"}}}});

  static void WritePack(string path,params (string Id,string Json)[] documents){using var zip=ZipFile.Open(path,ZipArchiveMode.Create);using(var writer=new StreamWriter(zip.CreateEntry("manifest.json").Open()))writer.Write("{\"schema\":\"comfy-quest-pack/v2\",\"pack_id\":\"guild\",\"version\":\"1.0.0\"}");foreach(var document in documents)using(var writer=new StreamWriter(zip.CreateEntry("experiences/"+document.Id+".json").Open()))writer.Write(document.Json);}
  static void Run(Action<string> body){var root=Path.Combine(Path.GetTempPath(),"comfy-continuation-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"inbox"));try{body(root);}finally{try{Directory.Delete(root,true);}catch{}}}

  sealed class BindingAdapter:IRuntimeBindingAdapter {
    public BindingAdapter(RuntimeBindingReference value){Value=Clone(value);}
    public RuntimeBindingReference Value;
    public IReadOnlyList<RuntimeBindingCandidate> ListCandidates()=>new[]{new RuntimeBindingCandidate{BindingZdo="10:20",TargetKind="sign",Label="sign",DistanceMetres=1}};
    public RuntimeBindingReference Read(string bindingZdo)=>Clone(Value);
    public bool TryWrite(string bindingZdo,RuntimeBindingReference reference,out string error){Value=Clone(reference);error=null;return true;}
    static RuntimeBindingReference Clone(RuntimeBindingReference value)=>value==null?null:new(){PackId=value.PackId,
      ExperienceId=value.ExperienceId,BindingId=value.BindingId,Version=value.Version,
      ContentHash=value.ContentHash,BindingInstanceId=value.BindingInstanceId};
  }
}
