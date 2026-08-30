namespace ComfyQuestRuntime;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using ComfyQuestContracts;
using Newtonsoft.Json.Linq;
using Xunit;

public sealed class DedicatedPersonalProgressionTests {
  const string WorldUid = "76561198000000001";
  static readonly string ContentHash = new('a', 64);
  static readonly WorldAuthority Peer = new() { IsPeerClient = true };

  [Fact]
  public void ExactPeerWorldContentAndMessageOnlyCampaignAreAccepted() {
    var decision = DedicatedPersonalProgressionPolicy.CanUse(
        Profile(), Peer, WorldUid, ContentHash, new[] { Hunt("air-drop"), Hunt("cold-shot") });
    Assert.True(decision.Allowed, decision.Diagnostic);
  }

  [Fact]
  public void DisabledHostWrongWorldAndWrongContentFailClosed() {
    Assert.Equal("dedicated_personal_progression_disabled",
        DedicatedPersonalProgressionPolicy.CanUse(
            new(), Peer, WorldUid, ContentHash, new[] { Hunt("air-drop") }).Diagnostic);
    Assert.Equal("dedicated_personal_progression_peer_required",
        DedicatedPersonalProgressionPolicy.CanUse(
            Profile(), new WorldAuthority { IsPrivateWorld = true, IsListenHost = true },
            WorldUid, ContentHash, new[] { Hunt("air-drop") }).Diagnostic);
    Assert.Equal("dedicated_personal_progression_world_mismatch",
        DedicatedPersonalProgressionPolicy.CanUse(
            Profile(), Peer, "76561198000000002", ContentHash,
            new[] { Hunt("air-drop") }).Diagnostic);
    Assert.Equal("dedicated_personal_progression_content_mismatch",
        DedicatedPersonalProgressionPolicy.CanUse(
            Profile(), Peer, WorldUid, new string('b', 64),
            new[] { Hunt("air-drop") }).Diagnostic);
  }

  [Theory]
  [InlineData("timer_start")]
  [InlineData("timer_cancel")]
  [InlineData("grant_item")]
  [InlineData("spawn")]
  [InlineData("clear_spawned")]
  public void EveryNonMessageActionIsRejected(string actionType) {
    var document = Hunt("air-drop");
    document.Stages[0].Transitions[0].Actions[0].Type = actionType;
    Assert.Equal("dedicated_personal_progression_action_denied",
        DedicatedPersonalProgressionPolicy.CanUse(
            Profile(), Peer, WorldUid, ContentHash, new[] { document }).Diagnostic);
  }

  [Fact]
  public void OnlyLocallyWitnessedHuntEventsEnterPersonalProgression() {
    var documents = new[] { Hunt("air-drop") };
    var kill = new RuntimeEvent { Name = "kill", Target = "$enemy_deathsquito" };
    Assert.True(DedicatedPersonalProgressionPolicy.CanAcceptEvent(
        Profile(), Peer, WorldUid, ContentHash, documents, kill, true).Allowed);
    Assert.Equal("dedicated_personal_progression_foreign_event",
        DedicatedPersonalProgressionPolicy.CanAcceptEvent(
            Profile(), Peer, WorldUid, ContentHash, documents, kill, false).Diagnostic);
    Assert.Equal("dedicated_personal_progression_event_denied",
        DedicatedPersonalProgressionPolicy.CanAcceptEvent(
            Profile(), Peer, WorldUid, ContentHash, documents,
            new RuntimeEvent { Name = "chat_received" }, true).Diagnostic);
  }

  [Fact]
  public void TwoPlayersPersistIndependentRunsAndReconnectToTheirOwnState() {
    var root = Path.Combine(Path.GetTempPath(), "comfy-personal-" + Guid.NewGuid().ToString("N"));
    try {
      var document = Hunt("air-drop");
      var coordinator = new RuntimeRunCoordinator(root);
      var firstScope = Scope("player-one");
      var secondScope = Scope("player-two");
      var first = coordinator.Resolve(firstScope, DateTimeOffset.UnixEpoch);
      var second = coordinator.Resolve(secondScope, DateTimeOffset.UnixEpoch);
      Assert.NotEqual(first.RunId, second.RunId);

      var workflows = new WorkflowStateStore(root);
      var decision = workflows.Begin(first.Identity(), document, new RuntimeEvent {
        Name = "kill", Target = "$enemy_deathsquito", At = DateTimeOffset.UnixEpoch,
        Fields = new Dictionary<string, string> { ["weapon_skill"] = "Spears", ["projectile"] = "true" },
      });
      Assert.NotNull(decision);
      Assert.True(workflows.Complete(decision));
      Assert.Equal("complete", workflows.Get(first.Identity()).Outcome);
      Assert.Null(workflows.Get(second.Identity()));

      var reconnected = new RuntimeRunCoordinator(root).Resolve(
          firstScope, DateTimeOffset.UnixEpoch.AddHours(1));
      Assert.Equal(first.RunId, reconnected.RunId);
      Assert.Equal("complete", new WorkflowStateStore(root).Get(reconnected.Identity()).Outcome);
    } finally {
      if (Directory.Exists(root)) Directory.Delete(root, true);
    }
  }

  [Fact]
  public void CheckedBetaPackCompilesAtTheProductionBoundaryAndFitsThePeerPolicy() {
    var source = Path.Combine(RepoRoot(), "creatoros", "beta1",
        "slayers-signature-hunt-1.0.0.questpack");
    var root = Path.Combine(Path.GetTempPath(), "comfy-beta-pack-" + Guid.NewGuid().ToString("N"));
    try {
      var inbox = Directory.CreateDirectory(Path.Combine(root, "inbox")).FullName;
      var path = Path.Combine(inbox, Path.GetFileName(source));
      File.Copy(source, path);
      var candidate = new QuestPackStore(root).Inspect(path);
      Assert.True(candidate.IsValid,
          string.Join("; ", candidate.Diagnostics.Select(value => value.Code + ":" + value.Message)));
      Assert.Equal("slayers-signature-hunt", candidate.Manifest.PackId);
      Assert.Equal("1.0.0", candidate.Manifest.Version);
      Assert.Equal(candidate.ContentHash, candidate.Manifest.ContentHash, ignoreCase: true);

      using var archive = ZipFile.OpenRead(path);
      Assert.True(ActiveExperienceResolver.TryResolveAll(archive, out var resolved, out var error), error);
      var documents = resolved.Select(value => ExperienceCompiler.CompileProductionJson(value.Json))
          .Select(compiled => {
            Assert.Empty(compiled.Diagnostics);
            return compiled.Document;
          }).ToArray();
      var profile = new DedicatedPersonalProgressionProfile {
        Enabled = true,
        WorldUid = WorldUid,
        ContentHash = candidate.ContentHash,
      };
      Assert.True(DedicatedPersonalProgressionPolicy.CanUse(
          profile, Peer, WorldUid, candidate.ContentHash, documents).Allowed);
      Assert.Equal("slayers-cold-shot", Assert.Single(documents.Single(
          value => value.Id == "slayers-air-drop").SuccessorExperienceIds));
      Assert.Equal("slayers-air-drop", Assert.Single(documents.Single(
          value => value.Id == "slayers-cold-shot").Prerequisites));
    } finally {
      if (Directory.Exists(root)) Directory.Delete(root, true);
    }
  }

  static DedicatedPersonalProgressionProfile Profile() => new() {
    Enabled = true,
    WorldUid = WorldUid,
    ContentHash = ContentHash,
  };

  static RuntimeRunScope Scope(string participant) => new() {
    WorldId = WorldUid,
    ExperienceId = "air-drop",
    BindingZdo = "1:9",
    BindingInstanceId = "binding-field-lodge",
    ContentHash = ContentHash,
    ParticipantIds = new List<string> { participant },
  };

  static ExperienceDocument Hunt(string id) => new() {
    Schema = ExperienceSchema.Id,
    Id = id,
    EntryStage = "hunt",
    Stages = new List<ExperienceStage> {
      new() {
        Id = "hunt",
        EntryActions = new List<ExperienceAction>(),
        Transitions = new List<ExperienceTransition> {
          new() {
            Id = "finish",
            Priority = 1,
            When = new TriggerExpression {
              Op = "EVENT",
              Event = "kill",
              Target = "$enemy_deathsquito",
              Where = new Dictionary<string, string> {
                ["weapon_skill"] = "Spears",
                ["projectile"] = "true",
              },
            },
            Actions = new List<ExperienceAction> {
              new() {
                Id = "done",
                Type = "message",
                Parameters = new Dictionary<string, JToken> { ["text"] = "Hunt complete." },
              },
            },
            Outcome = "complete",
          },
        },
      },
    },
    Bindings = new List<ExperienceBinding> {
      new() { Id = "default", ExperienceId = id, TargetKinds = new List<string> { "sign" } },
    },
  };

  static string RepoRoot() {
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "creatoros")))
      directory = directory.Parent;
    return directory?.FullName ?? ".";
  }
}
