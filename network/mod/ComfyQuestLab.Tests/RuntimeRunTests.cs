using System;
using System.Collections.Generic;
using System.IO;
using ComfyQuestContracts;
using Xunit;

public sealed class RuntimeRunTests : IDisposable
{
  readonly string root=Path.Combine(Path.GetTempPath(),"comfy-runtime-runs-"+Guid.NewGuid().ToString("N"));
  readonly DateTimeOffset now=DateTimeOffset.Parse("2026-08-24T18:00:00Z");

  [Fact]
  public void FreshRunPersistsOneExactScopeAndContentChangesCreateAnotherScope()
  {
    var first=new RuntimeRunCoordinator(root).Resolve(Scope(),now);
    Assert.StartsWith("run-",first.RunId);
    Assert.Equal(first.RunId,first.StateKey);
    Assert.False(first.LegacyStorage);
    Assert.Equal(first.RunId,new RuntimeRunCoordinator(root).Resolve(Scope(),now.AddMinutes(1)).RunId);

    var changed=Scope();
    changed.ContentHash="content-two";
    var second=new RuntimeRunCoordinator(root).Resolve(changed,now.AddMinutes(2));
    Assert.NotEqual(first.RunId,second.RunId);
    Assert.NotEqual(first.ScopeId,second.ScopeId);
    Assert.Equal(2,new RuntimeRunRegistry(root).List().Count);
  }

  [Fact]
  public void LegacyWorkflowIsAdoptedInPlaceWithoutDiscardingProgress()
  {
    var scope=Scope();
    var legacy=new WorkflowIdentity{WorldId=scope.WorldId,CharacterId=scope.CharacterId,BindingZdo=scope.BindingZdo,ContentHash=scope.ContentHash};
    Assert.Null(new WorkflowStateStore(root).Begin(legacy,Document(),new RuntimeEvent{Name="ignored",At=now}));
    new DurableTimerStore(root).Start(legacy,"legacy-clock",now.AddMinutes(1));
    Assert.True(new ActionExecutionLedger(root).TryClaim(legacy.Key+"|stage|route|reward"));

    var run=new RuntimeRunCoordinator(root).Resolve(scope,now.AddSeconds(1));
    Assert.StartsWith("legacy-",run.RunId);
    Assert.True(run.LegacyStorage);
    Assert.Equal(legacy.Key,run.StateKey);
    Assert.Equal("start",new WorkflowStateStore(root).Get(run.Identity()).StageId);
    Assert.Single(new DurableTimerStore(root).Pending(now,identity=>new RuntimeRunRegistry(root).IsActive(identity)));
  }

  [Fact]
  public void ConfirmedResetScopesEveryLedgerAndStartsARewardEligibleSuccessor()
  {
    var coordinator=new RuntimeRunCoordinator(root);
    var run=coordinator.Resolve(Scope(),now);
    SeedRun(run,1);
    var other=coordinator.Resolve(Scope("other-experience","other-content"),now.AddSeconds(1));
    SeedRun(other,9);
    var adapter=new FakeSpawnAdapter();
    var preview=coordinator.Preview(run.RunId,now.AddSeconds(2),adapter,noticeCount:3);

    Assert.True(preview.Snapshot.WorkflowPresent);
    Assert.Equal("start",preview.Snapshot.StageId);
    Assert.Single(preview.Snapshot.TimerIds);
    Assert.Equal(1,preview.Snapshot.ActionClaimCount);
    Assert.Single(preview.Snapshot.SpawnedObjects);
    Assert.Equal(3,preview.Snapshot.NoticeCount);
    Assert.Equal("retained",preview.Snapshot.PreviousRewards);
    Assert.Equal("per_run",preview.Snapshot.SuccessorRewardPolicy);

    var cleared=0;
    var ensured=new List<string>();
    var result=coordinator.Apply(run.RunId,preview.PreviewToken,now.AddSeconds(3),adapter,
      ()=>cleared++,successor=>{ensured.Add(successor.RunId);return null;});
    Assert.Equal("completed",result.State);
    Assert.Equal(run.RunId,result.PriorRunId);
    Assert.NotEqual(run.RunId,result.NewRunId);
    Assert.Equal(1,result.WorkflowStatesScoped);
    Assert.Equal(1,result.TimersScoped);
    Assert.Equal(1,result.ActionClaimsScoped);
    Assert.Equal(1,result.SpawnRowsCleaned);
    Assert.Equal(3,result.NoticesCleared);
    Assert.Equal(1,cleared);
    Assert.Equal(new[]{result.NewRunId},ensured);
    Assert.Equal("retained",result.PreviousRewards);
    Assert.Equal("per_run",result.SuccessorRewardPolicy);

    var registry=new RuntimeRunRegistry(root);
    Assert.Equal("reset",registry.Find(run.RunId).Status);
    var successor=registry.Find(result.NewRunId);
    Assert.Equal("active",successor.Status);
    Assert.Equal(run.ScopeId,successor.ScopeId);
    Assert.Equal(run.RunId,successor.PredecessorRunId);
    Assert.NotNull(new WorkflowStateStore(root).GetByKey(run.StateKey));
    Assert.Single(OwnedTimers(run.StateKey));
    Assert.Single(OwnedClaims(run.StateKey));
    Assert.Empty(OwnedSpawns(run.StateKey));
    Assert.Single(OwnedSpawns(other.StateKey));

    var due=new DurableTimerStore(root).Due(now.AddMinutes(2),registry.IsActive);
    Assert.DoesNotContain(due,value=>value.Identity.Key==run.StateKey);
    Assert.Contains(due,value=>value.Identity.Key==other.StateKey);
    Assert.True(new ActionExecutionLedger(root).TryClaim(successor.StateKey+"|stage|route|reward"));

    var replay=coordinator.Apply(run.RunId,preview.PreviewToken,now.AddMinutes(1),adapter,
      ()=>cleared++,successor=>{ensured.Add(successor.RunId);return null;});
    Assert.Equal(result.ResetId,replay.ResetId);
    Assert.Equal(result.NewRunId,replay.NewRunId);
    Assert.Equal(1,cleared);
    Assert.Equal(new[]{result.NewRunId,result.NewRunId},ensured);
  }

  [Fact]
  public void ConfirmedRetireCleansExactSpawnsAndCreatesNoSuccessor()
  {
    var coordinator=new RuntimeRunCoordinator(root);
    var run=coordinator.Resolve(Scope(),now);
    SeedRun(run,1);
    var other=coordinator.Resolve(Scope("other-experience","other-content"),now.AddSeconds(1));
    SeedRun(other,9);
    var adapter=new FakeSpawnAdapter();
    var preview=coordinator.PreviewRetire(run.RunId,now.AddSeconds(2),adapter,noticeCount:2);

    Assert.Equal("comfy-quest-runtime-retire-preview/v1",preview.Schema);
    Assert.Single(preview.Snapshot.SpawnedObjects);
    var cleared=0;
    var result=coordinator.ApplyRetire(run.RunId,preview.PreviewToken,now.AddSeconds(3),adapter,()=>cleared++);

    Assert.Equal("completed",result.State);
    Assert.Equal(run.RunId,result.RunId);
    Assert.False(result.SuccessorCreated);
    Assert.Equal(1,result.WorkflowStatesScoped);
    Assert.Equal(1,result.TimersScoped);
    Assert.Equal(1,result.ActionClaimsScoped);
    Assert.Equal(1,result.SpawnRowsCleaned);
    Assert.Equal(2,result.NoticesCleared);
    Assert.Equal(1,cleared);
    var registry=new RuntimeRunRegistry(root);
    Assert.Equal("retired",registry.Find(run.RunId).Status);
    Assert.Equal("undone",registry.Find(run.RunId).Outcome);
    Assert.Equal(result.RetireId,registry.Find(run.RunId).RetireId);
    Assert.Equal(2,registry.List().Count);
    Assert.Null(new WorkflowStateStore(root).GetByKey(run.StateKey));
    Assert.Empty(OwnedTimers(run.StateKey));
    Assert.Empty(OwnedClaims(run.StateKey));
    Assert.Empty(OwnedSpawns(run.StateKey));
    Assert.NotNull(new WorkflowStateStore(root).GetByKey(other.StateKey));
    Assert.Single(OwnedTimers(other.StateKey));
    Assert.Single(OwnedClaims(other.StateKey));
    Assert.Single(OwnedSpawns(other.StateKey));

    var replay=coordinator.ApplyRetire(run.RunId,preview.PreviewToken,now.AddMinutes(1),adapter,()=>cleared++);
    Assert.Equal(result.RetireId,replay.RetireId);
    Assert.Equal(1,cleared);
    Assert.Equal(2,registry.List().Count);
  }

  [Fact]
  public void RetireResumesTheSameTransactionAfterEphemeraCleanupFails()
  {
    var coordinator=new RuntimeRunCoordinator(root);
    var run=coordinator.Resolve(Scope(),now);
    SeedRun(run,1);
    var adapter=new FakeSpawnAdapter();
    var preview=coordinator.PreviewRetire(run.RunId,now.AddSeconds(1),adapter);

    var incomplete=coordinator.ApplyRetire(run.RunId,preview.PreviewToken,
      now.AddSeconds(2),adapter,()=>throw new InvalidOperationException("ephemera_busy"));

    Assert.Equal("cleanup_incomplete",incomplete.State);
    Assert.Equal("ephemera_busy",incomplete.Detail);
    Assert.Equal("retired",new RuntimeRunRegistry(root).Find(run.RunId).Status);
    Assert.Null(new WorkflowStateStore(root).GetByKey(run.StateKey));
    Assert.Empty(OwnedTimers(run.StateKey));
    Assert.Empty(OwnedClaims(run.StateKey));
    Assert.Empty(OwnedSpawns(run.StateKey));

    var cleared=0;
    var completed=coordinator.ApplyRetire(run.RunId,preview.PreviewToken,
      now.AddSeconds(3),adapter,()=>cleared++);
    Assert.Equal("completed",completed.State);
    Assert.Equal(incomplete.RetireId,completed.RetireId);
    Assert.Equal(1,cleared);
  }

  [Fact]
  public void SuccessorStartFailureRemainsResumableAndReusesTheExactSuccessor()
  {
    var coordinator=new RuntimeRunCoordinator(root);
    var run=coordinator.Resolve(Scope(),now);
    SeedRun(run,1);
    var preview=coordinator.Preview(run.RunId,now.AddSeconds(1),new FakeSpawnAdapter());
    var attempts=new List<string>();

    var incomplete=coordinator.Apply(run.RunId,preview.PreviewToken,now.AddSeconds(2),
      new FakeSpawnAdapter(),ensureSuccessorStarted:successor=>{
        attempts.Add(successor.RunId);
        return "binding_start_selection_mismatch";
      });

    Assert.Equal("successor_start_incomplete",incomplete.State);
    Assert.Equal("binding_start_selection_mismatch",incomplete.Detail);
    Assert.False(string.IsNullOrWhiteSpace(incomplete.NewRunId));
    Assert.Equal("reset",new RuntimeRunRegistry(root).Find(run.RunId).Status);
    Assert.Equal("active",new RuntimeRunRegistry(root).Find(incomplete.NewRunId).Status);
    Assert.Equal(2,new RuntimeRunRegistry(root).List().Count);
    var transactionPath=Path.Combine(root,"state","reset-transactions",preview.PreviewToken+".json");
    var pending=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(transactionPath));
    Assert.Null(pending["result"]);

    var completed=coordinator.Apply(run.RunId,preview.PreviewToken,now.AddMinutes(10),
      new FakeSpawnAdapter(),ensureSuccessorStarted:successor=>{
        attempts.Add(successor.RunId);
        return null;
      });

    Assert.Equal("completed",completed.State);
    Assert.Equal(incomplete.ResetId,completed.ResetId);
    Assert.Equal(incomplete.NewRunId,completed.NewRunId);
    Assert.Equal(new[]{completed.NewRunId,completed.NewRunId},attempts);
    Assert.Equal(2,new RuntimeRunRegistry(root).List().Count);
  }

  [Fact]
  public void CompletedResetCanRepairItsSuccessorWithoutMintingAnotherRun()
  {
    var coordinator=new RuntimeRunCoordinator(root);
    var run=coordinator.Resolve(Scope(),now);
    SeedRun(run,1);
    var adapter=new FakeSpawnAdapter();
    var preview=coordinator.Preview(run.RunId,now.AddSeconds(1),adapter);
    var completed=coordinator.Apply(run.RunId,preview.PreviewToken,now.AddSeconds(2),adapter);
    Assert.Equal("completed",completed.State);

    var repairFailure=coordinator.Apply(run.RunId,preview.PreviewToken,now.AddSeconds(3),adapter,
      ensureSuccessorStarted:successor=>{
        Assert.Equal(completed.NewRunId,successor.RunId);
        return "binding_start_not_observed";
      });

    Assert.Equal("successor_start_incomplete",repairFailure.State);
    Assert.Equal("binding_start_not_observed",repairFailure.Detail);
    Assert.Equal(completed.ResetId,repairFailure.ResetId);
    Assert.Equal(completed.NewRunId,repairFailure.NewRunId);
    var transactionPath=Path.Combine(root,"state","reset-transactions",preview.PreviewToken+".json");
    var retained=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(transactionPath));
    Assert.Equal("completed",(string)retained["result"]?["state"]);

    var repaired=coordinator.Apply(run.RunId,preview.PreviewToken,now.AddSeconds(4),adapter,
      ensureSuccessorStarted:successor=>{
        Assert.Equal(completed.NewRunId,successor.RunId);
        return null;
      });

    Assert.Equal("completed",repaired.State);
    Assert.Equal(completed.ResetId,repaired.ResetId);
    Assert.Equal(completed.NewRunId,repaired.NewRunId);
    Assert.Equal(2,new RuntimeRunRegistry(root).List().Count);
  }

  [Fact]
  public void PreviewBecomesStaleWhenScopedStateChanges()
  {
    var coordinator=new RuntimeRunCoordinator(root);
    var run=coordinator.Resolve(Scope(),now);
    SeedRun(run,1);
    var preview=coordinator.Preview(run.RunId,now.AddSeconds(1),new FakeSpawnAdapter());
    new DurableTimerStore(root).Start(run.Identity(),"second-clock",now.AddMinutes(2));

    var error=Assert.Throws<InvalidOperationException>(()=>coordinator.Apply(run.RunId,preview.PreviewToken,now.AddSeconds(2),new FakeSpawnAdapter()));
    Assert.Equal("reset_preview_stale",error.Message);
    Assert.Equal("active",new RuntimeRunRegistry(root).Find(run.RunId).Status);
  }

  [Fact]
  public void PartialSpawnCleanupResumesAfterPreviewExpiry()
  {
    var coordinator=new RuntimeRunCoordinator(root);
    var run=coordinator.Resolve(Scope(),now);
    SeedRun(run,1);
    var actionKey=run.StateKey+"|stage|route|spawn-two";
    new SpawnExecutionStore(root).Record(Spawn(run,actionKey,2));
    var adapter=new FakeSpawnAdapter(failOnceObjectId:2);
    var preview=coordinator.Preview(run.RunId,now.AddSeconds(1),adapter);

    var partial=coordinator.Apply(run.RunId,preview.PreviewToken,now.AddSeconds(2),adapter);
    Assert.Equal("cleanup_incomplete",partial.State);
    Assert.Equal(1,partial.SpawnRowsCleaned);
    Assert.Single(OwnedSpawns(run.StateKey));
    Assert.Equal("active",new RuntimeRunRegistry(root).Find(run.RunId).Status);

    var completed=coordinator.Apply(run.RunId,preview.PreviewToken,now.AddMinutes(10),adapter);
    Assert.Equal("completed",completed.State);
    Assert.Equal(2,completed.SpawnRowsCleaned);
    Assert.Empty(OwnedSpawns(run.StateKey));
    Assert.Equal("reset",new RuntimeRunRegistry(root).Find(run.RunId).Status);
  }

  [Fact]
  public void AmbiguousSpawnOwnershipBlocksEvenThePreview()
  {
    var coordinator=new RuntimeRunCoordinator(root);
    var run=coordinator.Resolve(Scope(),now);
    SeedRun(run,1);
    var error=Assert.Throws<InvalidOperationException>(()=>coordinator.Preview(run.RunId,now,new AmbiguousSpawnAdapter()));
    Assert.Equal("spawn_cleanup_ambiguous",error.Message);
    Assert.Equal("active",new RuntimeRunRegistry(root).Find(run.RunId).Status);
  }

  [Fact]
  public void ARecoveredTransactionCannotSubstituteAnotherRunsSpawnLedger()
  {
    var coordinator=new RuntimeRunCoordinator(root);
    var run=coordinator.Resolve(Scope(),now);
    SeedRun(run,1);
    var other=coordinator.Resolve(Scope("other-experience","other-content"),now.AddSeconds(1));
    SeedRun(other,9);
    var preview=coordinator.Preview(run.RunId,now.AddSeconds(2),new FakeSpawnAdapter());
    var directory=Directory.CreateDirectory(Path.Combine(root,"state","reset-transactions")).FullName;
    File.WriteAllText(Path.Combine(directory,preview.PreviewToken+".json"),Newtonsoft.Json.JsonConvert.SerializeObject(new
    {
      schema="comfy-quest-runtime-reset-transaction/v1",
      reset_id="reset-forged",
      preview,
      pending=new[]{Assert.Single(OwnedSpawns(other.StateKey))},
      cleaned=Array.Empty<SpawnedObject>(),
    }));

    var error=Assert.Throws<InvalidDataException>(()=>coordinator.Apply(run.RunId,preview.PreviewToken,now.AddSeconds(3),new FakeSpawnAdapter()));
    Assert.Equal("reset_transaction_scope_invalid",error.Message);
    Assert.Single(OwnedSpawns(run.StateKey));
    Assert.Single(OwnedSpawns(other.StateKey));
    Assert.Equal("active",new RuntimeRunRegistry(root).Find(run.RunId).Status);
  }

  [Fact]
  public void LegacyAdoptionFailsClosedWhenAnyLedgerIsUnreadable()
  {
    var directory=Directory.CreateDirectory(Path.Combine(root,"state")).FullName;
    File.WriteAllText(Path.Combine(directory,"timers.json"),"not json");
    var error=Assert.Throws<InvalidDataException>(()=>new RuntimeRunCoordinator(root).Resolve(Scope(),now));
    Assert.Equal("timer_state_unreadable",error.Message);
    Assert.Empty(new RuntimeRunRegistry(root).List());
  }

  [Fact]
  public void CorruptDurableStoresCannotMasqueradeAsEmptyOrDuplicateState()
  {
    var directory=Directory.CreateDirectory(Path.Combine(root,"state")).FullName;
    File.WriteAllText(Path.Combine(directory,"workflow-states.json"),"not json");
    File.WriteAllText(Path.Combine(directory,"timers.json"),"not json");
    File.WriteAllText(Path.Combine(directory,"executed-actions.json"),"not json");
    File.WriteAllText(Path.Combine(directory,"spawned-objects.json"),"not json");
    var identity=new WorkflowIdentity{WorldId="123",CharacterId="hero",BindingZdo="10:20",ContentHash="content-one"};
    Assert.Equal("workflow_state_unreadable",Assert.Throws<InvalidDataException>(()=>new WorkflowStateStore(root).Begin(identity,Document(),new RuntimeEvent{Name="ignored",At=now})).Message);
    Assert.Equal("timer_state_unreadable",Assert.Throws<InvalidDataException>(()=>new DurableTimerStore(root).Pending(now)).Message);
    Assert.Equal("action_state_unreadable",Assert.Throws<InvalidDataException>(()=>new ActionExecutionLedger(root).TryClaim("run|stage|route|reward")).Message);
    Assert.Equal("spawn_state_unreadable",Assert.Throws<InvalidDataException>(()=>new SpawnExecutionStore(root).ForOwnerAction("run","spawn")).Message);
  }

  [Theory]
  [InlineData("workflow-states.json","{\"schema\":\"wrong\",\"states\":{}}","workflow_state_unreadable")]
  [InlineData("timers.json","{\"schema\":\"wrong\",\"timers\":{}}","timer_state_unreadable")]
  [InlineData("executed-actions.json","{\"schema\":\"wrong\",\"keys\":[]}","action_state_unreadable")]
  [InlineData("spawned-objects.json","{\"schema\":\"wrong\",\"objects\":[]}","spawn_state_unreadable")]
  public void WrongLedgerSchemasCannotMasqueradeAsEmptyState(string filename,string json,string expected)
  {
    var directory=Directory.CreateDirectory(Path.Combine(root,"state")).FullName;
    File.WriteAllText(Path.Combine(directory,filename),json);
    var error=Assert.Throws<InvalidDataException>(()=>new RuntimeRunCoordinator(root).Resolve(Scope(),now));
    Assert.Equal(expected,error.Message);
    Assert.Empty(new RuntimeRunRegistry(root).List());
  }

  [Fact]
  public void RunControlPolicyRequiresExactIdentityFreshnessAndConfirmation()
  {
    var valid=new RuntimeRunControlRequest{RequestId="request-1",Operation="apply_reset",CreatedUtc=now.ToString("O"),ExpiresUtc=now.AddMinutes(2).ToString("O"),ExpectedMachine="OMEN",ExpectedWorldUid="123",CreatorSessionId="creator-session-one",RunId="run-one",PreviewToken="rstp-one",ConfirmReset=true};
    Assert.True(RuntimeRunControlRequestPolicy.Validate(valid,now,out var accepted),accepted);
    valid.CreatorSessionId=null;
    Assert.False(RuntimeRunControlRequestPolicy.Validate(valid,now,out var session));
    Assert.Equal("request_identity_invalid",session);
    valid.CreatorSessionId="creator-session-one";
    valid.ConfirmReset=false;
    Assert.False(RuntimeRunControlRequestPolicy.Validate(valid,now,out var confirmation));
    Assert.Equal("reset_confirmation_required",confirmation);
    valid.ConfirmReset=true;
    valid.ExpiresUtc=now.AddMinutes(20).ToString("O");
    Assert.False(RuntimeRunControlRequestPolicy.Validate(valid,now,out var expiry));
    Assert.Equal("request_time_invalid",expiry);
    valid.RequestId="..\\outside";
    Assert.False(RuntimeRunControlRequestPolicy.CanAddressReceipt(valid));

    var retire=new RuntimeRunControlRequest{RequestId="request-2",Operation="apply_retire",CreatedUtc=now.ToString("O"),ExpiresUtc=now.AddMinutes(2).ToString("O"),ExpectedMachine="OMEN",ExpectedWorldUid="123",CreatorSessionId="creator-session-one",RunId="run-one",PreviewToken="retp-one",ConfirmRetire=true};
    Assert.True(RuntimeRunControlRequestPolicy.Validate(retire,now,out var retireAccepted),retireAccepted);
    retire.ConfirmRetire=false;
    Assert.False(RuntimeRunControlRequestPolicy.Validate(retire,now,out var retireConfirmation));
    Assert.Equal("retire_confirmation_required",retireConfirmation);
  }

  void SeedRun(RuntimeRunRecord run,uint objectId)
  {
    Assert.Null(new WorkflowStateStore(root).Begin(run.Identity(),Document(),new RuntimeEvent{Name="ignored",At=now}));
    new DurableTimerStore(root).Start(run.Identity(),"clock",now.AddMinutes(1));
    Assert.True(new ActionExecutionLedger(root).TryClaim(run.StateKey+"|stage|route|reward"));
    var actionKey=run.StateKey+"|stage|route|spawn";
    new SpawnExecutionStore(root).Record(Spawn(run,actionKey,objectId));
  }

  SpawnedObject Spawn(RuntimeRunRecord run,string actionKey,uint objectId)=>new(){ActionKey=actionKey,RunId=run.RunId,WorldId=run.Scope.WorldId,ExperienceId=run.Scope.ExperienceId,ContentHash=run.Scope.ContentHash,ActionId="spawn",Kind="piece",Prefab="wood_wall",UserId=7,ObjectId=objectId};
  IReadOnlyList<DurableTimer> OwnedTimers(string key){Assert.True(new DurableTimerStore(root).TryForOwner(key,out var rows));return rows;}
  IReadOnlyList<string> OwnedClaims(string key){Assert.True(new ActionExecutionLedger(root).TryForOwner(key,out var rows));return rows;}
  IReadOnlyList<SpawnedObject> OwnedSpawns(string key){Assert.True(new SpawnExecutionStore(root).TryForOwner(key,out var rows));return rows;}

  static RuntimeRunScope Scope(string experience="experience-one",string content="content-one")=>new(){WorldId="123",ExperienceId=experience,BindingZdo="10:20",ParticipantIds=new(){"hero"},ContentHash=content};
  static ExperienceDocument Document()=>new(){Schema=ExperienceSchema.Id,Id="experience-one",Title="Run test",EntryStage="start",Stages=new(){new ExperienceStage{Id="start",EntryActions=new(),Transitions=new()}},Bindings=new()};

  public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}

  sealed class FakeSpawnAdapter(uint? failOnceObjectId=null):IRuntimeSpawnResetAdapter
  {
    readonly HashSet<uint> absent=new();
    bool failed;
    public RuntimeSpawnResetObservation Inspect(SpawnedObject value)=>new(){State=absent.Contains(value.ObjectId)?"already_absent":"owned"};
    public RuntimeSpawnResetObservation Cleanup(SpawnedObject value)
    {
      if(failOnceObjectId==value.ObjectId&&!failed){failed=true;return new(){State="foreign",Detail="ownership_changed"};}
      if(!absent.Add(value.ObjectId))return new(){State="already_absent"};
      return new(){State="owned"};
    }
  }

  sealed class AmbiguousSpawnAdapter:IRuntimeSpawnResetAdapter
  {
    public RuntimeSpawnResetObservation Inspect(SpawnedObject value)=>new(){State="foreign",Detail="not_owned"};
    public RuntimeSpawnResetObservation Cleanup(SpawnedObject value)=>new(){State="foreign",Detail="not_owned"};
  }
}
