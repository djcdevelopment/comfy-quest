namespace ComfyNetworkSense.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using ComfyQuestContracts;
using Xunit;

public sealed class RuntimeBindingTests {
  const string Hash="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

  [Fact]
  public void PrerequisitesAreExactToWorldContentAndBindingThenRestoreIsIdempotent() {
    Run(root=>{
      var adapter=new FakeAdapter(new RuntimeBindingCandidate{BindingZdo="10:20",TargetKind="sign",Label="Runestone",DistanceMetres=3});
      var registry=new RuntimeRunRegistry(root);
      var coordinator=new RuntimeBindingCoordinator(root,adapter,registry);
      var active=Active("beta");
      var beta=Document("beta","alpha");
      var blocked=Assert.Throws<InvalidOperationException>(()=>coordinator.Bind("10:20","123",active,beta,DateTimeOffset.UtcNow));
      Assert.Equal("experience_prerequisite_incomplete:alpha",blocked.Message);

      Complete(registry,"alpha","999","10:20");
      Complete(registry,"alpha","123","10:20","bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
      Complete(registry,"alpha","123","99:1");
      Assert.Equal("experience_prerequisite_incomplete:alpha",Assert.Throws<InvalidOperationException>(
        ()=>coordinator.Bind("10:20","123",active,beta,DateTimeOffset.UtcNow)).Message);
      Complete(registry,"alpha","123","10:20");
      var change=coordinator.Bind("10:20","123",active,beta,DateTimeOffset.UtcNow);
      Assert.Equal("applied",change.State);
      Assert.Equal("beta",adapter.Read("10:20").ExperienceId);
      Assert.True(File.Exists(Path.Combine(root,"state","binding-changes",change.ChangeId+".json")));

      var restored=coordinator.Restore("10:20",change.ChangeId,"123",DateTimeOffset.UtcNow);
      Assert.Equal("restored",restored.State);
      Assert.Null(adapter.Read("10:20").ExperienceId);
      Assert.Equal("restored",coordinator.Restore("10:20",change.ChangeId,"123",DateTimeOffset.UtcNow).State);
    });
  }

  [Fact]
  public void PendingTransactionRecoversFromAnAppliedWorldWriteButRefusesUnrelatedState() {
    Run(root=>{
      var adapter=new FakeAdapter(new RuntimeBindingCandidate{BindingZdo="10:20",TargetKind="sign",Label="Runestone",DistanceMetres=3});
      var coordinator=new RuntimeBindingCoordinator(root,adapter);
      var change=coordinator.Bind("10:20","123",Active("alpha"),Document("alpha"),DateTimeOffset.UtcNow);
      var path=Path.Combine(root,"state","binding-changes",change.ChangeId+".json");
      var persisted=JsonConvert.DeserializeObject<RuntimeBindingChange>(File.ReadAllText(path));
      persisted.State="pending";
      File.WriteAllText(path,JsonConvert.SerializeObject(persisted,Formatting.Indented));
      adapter.Force("10:20",new RuntimeBindingReference{PackId=persisted.Applied.PackId,
        ExperienceId=persisted.Applied.ExperienceId});
      Assert.Equal("restored",coordinator.Restore("10:20",change.ChangeId,"123",DateTimeOffset.UtcNow).State);

      var second=coordinator.Bind("10:20","123",Active("alpha"),Document("alpha"),DateTimeOffset.UtcNow);
      adapter.Force("10:20",new RuntimeBindingReference{PackId="foreign",ExperienceId="foreign",BindingId="default",Version="1.0.0",ContentHash=Hash});
      Assert.Equal("binding_restore_state_changed",Assert.Throws<InvalidOperationException>(
        ()=>coordinator.Restore("10:20",second.ChangeId,"123",DateTimeOffset.UtcNow)).Message);
    });
  }

  [Fact]
  public void AFailedPartialWorldWriteRestoresFromTheWriteAheadRecord() {
    Run(root=>{
      var adapter=new FakeAdapter(new RuntimeBindingCandidate{BindingZdo="10:20",TargetKind="sign",Label="Runestone",DistanceMetres=3})
        {FailNextWritePartially=true};
      var coordinator=new RuntimeBindingCoordinator(root,adapter);
      Assert.Equal("synthetic_partial_write",Assert.Throws<InvalidOperationException>(
        ()=>coordinator.Bind("10:20","123",Active("alpha"),Document("alpha"),DateTimeOffset.UtcNow)).Message);
      Assert.Null(adapter.Read("10:20").PackId);
      var change=Assert.Single(Directory.GetFiles(Path.Combine(root,"state","binding-changes"),"binding-*.json"));
      Assert.Equal("restored",JsonConvert.DeserializeObject<RuntimeBindingChange>(File.ReadAllText(change)).State);
    });
  }

  [Fact]
  public void AFailedPartialRestoreRemainsPendingAndCanResume() {
    Run(root=>{
      var adapter=new FakeAdapter(new RuntimeBindingCandidate{BindingZdo="10:20",TargetKind="sign",Label="Runestone",DistanceMetres=3});
      var coordinator=new RuntimeBindingCoordinator(root,adapter);
      var change=coordinator.Bind("10:20","123",Active("alpha"),Document("alpha"),DateTimeOffset.UtcNow);
      adapter.FailNextWritePartially=true;
      Assert.Equal("synthetic_partial_write",Assert.Throws<InvalidOperationException>(
        ()=>coordinator.Restore("10:20",change.ChangeId,"123",DateTimeOffset.UtcNow)).Message);
      var path=Path.Combine(root,"state","binding-changes",change.ChangeId+".json");
      Assert.Equal("pending",JsonConvert.DeserializeObject<RuntimeBindingChange>(File.ReadAllText(path)).State);
      Assert.Equal("restored",coordinator.Restore("10:20",change.ChangeId,"123",DateTimeOffset.UtcNow).State);
      Assert.Null(adapter.Read("10:20").PackId);
    });
  }

  [Fact]
  public void CandidateListIsDeterministicBoundedAndClosed() {
    Run(root=>{
      var adapter=new FakeAdapter(
        new RuntimeBindingCandidate{BindingZdo="10:2",TargetKind="sign",Label="Far",DistanceMetres=8},
        new RuntimeBindingCandidate{BindingZdo="10:1",TargetKind="player_built_piece",Label="Near",DistanceMetres=2});
      var values=new RuntimeBindingCoordinator(root,adapter).Candidates();
      Assert.Equal(new[]{"10:1","10:2"},values.Select(value=>value.BindingZdo));
      adapter.Candidates=Enumerable.Range(0,RuntimeBindingCoordinator.MaxCandidates+1).Select(index=>new RuntimeBindingCandidate{BindingZdo="10:"+(index+1),TargetKind="sign",Label="Sign",DistanceMetres=1}).ToList();
      Assert.Equal("binding_candidate_limit",Assert.Throws<InvalidOperationException>(()=>new RuntimeBindingCoordinator(root,adapter).Candidates()).Message);
    });
  }

  static ActiveSet Active(string experience)=>new(){PackId="guild",Version="1.0.0",ContentHash=Hash,ExperienceId=experience};
  static ExperienceDocument Document(string id,params string[] prerequisites)=>new(){Schema=ExperienceSchema.Id,Id=id,EntryStage="start",Prerequisites=prerequisites.ToList(),Stages=new(){new ExperienceStage{Id="start",Transitions=new()}},Bindings=new(){new ExperienceBinding{Id="default",ExperienceId=id,TargetKinds=new(){"sign"}}}};
  static void Complete(RuntimeRunRegistry registry,string experience,string world,string binding,string hash=Hash){var run=registry.Resolve(new RuntimeRunScope{WorldId=world,ExperienceId=experience,BindingZdo=binding,ContentHash=hash,ParticipantIds=new(){"hero"}},false,DateTimeOffset.UtcNow);registry.MarkOutcome(run.RunId,"complete",DateTimeOffset.UtcNow);}
  static void Run(Action<string> body){var root=Path.Combine(Path.GetTempPath(),"comfy-binding-"+Guid.NewGuid().ToString("N"));try{body(root);}finally{try{Directory.Delete(root,true);}catch{}}}

  sealed class FakeAdapter : IRuntimeBindingAdapter {
    readonly Dictionary<string,RuntimeBindingReference> values=new(StringComparer.Ordinal);
    public FakeAdapter(params RuntimeBindingCandidate[] candidates){Candidates=candidates.ToList();}
    public List<RuntimeBindingCandidate> Candidates {get;set;}
    public bool FailNextWritePartially {get;set;}
    public IReadOnlyList<RuntimeBindingCandidate> ListCandidates()=>Candidates;
    public RuntimeBindingReference Read(string bindingZdo)=>values.TryGetValue(bindingZdo,out var value)?Clone(value):new RuntimeBindingReference();
    public bool TryWrite(string bindingZdo,RuntimeBindingReference reference,out string error){
      if(FailNextWritePartially){FailNextWritePartially=false;var partial=Read(bindingZdo);partial.PackId=reference.PackId;partial.ExperienceId=reference.ExperienceId;values[bindingZdo]=partial;error="synthetic_partial_write";return false;}
      values[bindingZdo]=Clone(reference);error=null;return true;
    }
    public void Force(string bindingZdo,RuntimeBindingReference reference)=>values[bindingZdo]=Clone(reference);
    static RuntimeBindingReference Clone(RuntimeBindingReference value)=>new(){PackId=value.PackId,ExperienceId=value.ExperienceId,BindingId=value.BindingId,Version=value.Version,ContentHash=value.ContentHash};
  }
}
