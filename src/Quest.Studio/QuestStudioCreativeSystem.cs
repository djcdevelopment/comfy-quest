using System.Security.Cryptography;
using System.Text;

namespace Comfy.Quest.Studio;

/// <summary>Lossless, immutable absorption of one community-owned catalog release.</summary>
public sealed class StudioGuildSourceSnapshot
{
    public string SourceSnapshotId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Guild { get; set; } = string.Empty;
    public int Era { get; set; }
    public string SourceKind { get; set; } = string.Empty;
    public string SnapshotHash { get; set; } = string.Empty;
    public string CatalogJson { get; set; } = string.Empty;
    public string CatalogSha256 { get; set; } = string.Empty;
    public string ProvenanceJson { get; set; } = string.Empty;
    public string ProvenanceSha256 { get; set; } = string.Empty;
    public string AnomaliesText { get; set; } = string.Empty;
    public string AnomaliesSha256 { get; set; } = string.Empty;
    public int EntryCount { get; set; }
    public int AnomalyCount { get; set; }
    public DateTimeOffset ImportedUtc { get; set; }
}

public sealed class StudioGuildSourceRuling
{
    public string SourceSnapshotId { get; set; } = string.Empty;
    public string AnomalyKey { get; set; } = string.Empty;
    public string Disposition { get; set; } = "acknowledged";
    public string Note { get; set; } = string.Empty;
    public DateTimeOffset RuledUtc { get; set; }
}

public sealed class StudioGuildAbstractionDocument
{
    public string AbstractionId { get; set; } = string.Empty;
    public int Revision { get; set; } = 1;
    public string ContentHash { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public string SourceSnapshotId { get; set; } = string.Empty;
    public string SourceSnapshotHash { get; set; } = string.Empty;
    public List<string> SourceQuestIds { get; set; } = new();
    public string Attribution { get; set; } = string.Empty;
    public string EvidencePolicy { get; set; } = "preserve_source";
    public string EvidenceExplanation { get; set; } = string.Empty;
    public string CanonicalProjectJson { get; set; } = string.Empty;
    public string CanonicalProjectHash { get; set; } = string.Empty;
    public string InvariantHash { get; set; } = string.Empty;
    public string EntryNodeId { get; set; } = string.Empty;
    public string RouteId { get; set; } = string.Empty;
    public string? CompletionActionId { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetSpawnActionId { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool ConfigurablePractice { get; set; }
    public List<StudioAbstractionTargetChoice> TargetChoices { get; set; } = new();
    public DateTimeOffset PublishedUtc { get; set; }
}

public sealed class StudioAbstractionTargetChoice
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string RuntimeTarget { get; set; } = string.Empty;
    public string SourceQuestId { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? PracticeAttribution { get; set; }
}

public sealed class StudioGuildPaletteEntry
{
    public string AbstractionId { get; set; } = string.Empty;
    public int Revision { get; set; }
    public string ContentHash { get; set; } = string.Empty;
}

public sealed class StudioGuildArtifactMembership
{
    public string ProjectId { get; set; } = string.Empty;
    public string Kind { get; set; } = "quest";
    public string Creator { get; set; } = string.Empty;
    public string? AbstractionId { get; set; }
    public int? AbstractionRevision { get; set; }
    public bool RequiresGuildCompliance { get; set; }
}

public sealed class StudioCampaignDocument
{
    public string CampaignId { get; set; } = string.Empty;
    public int Revision { get; set; } = 1;
    public DateTimeOffset UpdatedUtc { get; set; }
    public string PackId { get; set; } = string.Empty;
    public string Version { get; set; } = "0.1.0";
    public string Title { get; set; } = string.Empty;
    public string Creator { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ReleaseState { get; set; } = "draft";
    public List<StudioQuestline> Questlines { get; set; } = new();
    public List<StudioGuildArtifact> StandaloneQuests { get; set; } = new();
    public List<StudioGuildArtifact> Events { get; set; } = new();
}

public sealed class StudioProjectDerivation
{
    public string GuildId { get; set; } = string.Empty;
    public string AbstractionId { get; set; } = string.Empty;
    public int AbstractionRevision { get; set; }
    public string AbstractionHash { get; set; } = string.Empty;
    public string SourceSnapshotId { get; set; } = string.Empty;
    public string SourceSnapshotHash { get; set; } = string.Empty;
    public string ConfigurationHash { get; set; } = string.Empty;
    public string TargetChoiceId { get; set; } = string.Empty;
    public string TargetRuntimeValue { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public string CompletionMessage { get; set; } = string.Empty;
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Mechanic { get; set; }
    public bool Detached { get; set; }
    public DateTimeOffset InstantiatedUtc { get; set; }
}

public sealed record StudioSourceImportRequest(
    int ExpectedRevision,
    string? SourceSnapshotId,
    string? Title,
    string? CatalogJson,
    string? ProvenanceJson,
    string? AnomaliesText);

public sealed record StudioSourceImportResult(bool Ok, bool Conflict, string? Error, StudioGuildDocument? Guild, StudioGuildSourceSnapshot? Snapshot);

public sealed record StudioAbstractionPromoteRequest(
    int ExpectedRevision,
    string? AbstractionId,
    string? Title,
    string? Explanation,
    string? SourceSnapshotId,
    IReadOnlyList<string>? SourceQuestIds,
    string? ProjectId,
    string? RouteId,
    string? CompletionActionId,
    string? Attribution,
    string? EvidencePolicy,
    string? EvidenceExplanation,
    IReadOnlyList<StudioAbstractionTargetChoice>? TargetChoices);

public sealed record StudioAbstractionReviseRequest(
    int ExpectedRevision,
    string? EvidencePolicy,
    string? EvidenceExplanation,
    bool StageOwnedTarget = false,
    bool IncludePracticeLox = false,
    bool IncludePracticeDraugr = false,
    bool ConfigurablePractice = false,
    IReadOnlyList<string>? PracticeTargets = null);

public sealed record StudioAbstractionMutationResult(bool Ok, bool Conflict, string? Error, StudioGuildDocument? Guild, StudioGuildAbstractionDocument? Abstraction);

public sealed record StudioAbstractionInstantiateRequest(
    int ExpectedGuildRevision,
    int AbstractionRevision,
    string? Title,
    string? TargetChoiceId,
    string? Instructions,
    string? CompletionMessage,
    string? Creator,
    string? Mechanic = null);

public sealed record StudioAbstractionInstantiateResult(bool Ok, bool Conflict, string? Error, StudioGuildDocument? Guild, StudioProjectDocument? Project);

public sealed record StudioHuntConfigureRequest(int ExpectedRevision, string? Title,
    string? TargetChoiceId, string? Instructions, string? CompletionMessage, string? Mechanic = null);

public sealed record StudioProjectDetachRequest(int ExpectedRevision);

public sealed record StudioCampaignCreateRequest(int ExpectedGuildRevision, string? Title, string? Creator);
public sealed record StudioCampaignSaveRequest(int ExpectedRevision, StudioCampaignDocument? Campaign);
public sealed record StudioCampaignPlacementRequest(
    int ExpectedRevision,
    string ProjectId,
    string Kind,
    string ContainerKind,
    string? QuestlineId,
    string? ProgressionBandId);
public sealed record StudioCampaignMutationResult(bool Ok, bool Conflict, string? Error, StudioGuildDocument? Guild, StudioCampaignDocument? Campaign);
public sealed record StudioCampaignPublishRequest(int ExpectedRevision);
public sealed record StudioCampaignCertificationResult(
    bool Ok,
    string? Error,
    StudioCampaignDocument? Campaign,
    string? ContentHash,
    IReadOnlyList<string> ExperienceIds,
    StudioCampaignCompilationReceipt? CompilationReceipt,
    IReadOnlyList<ComfyQuestContracts.ContractDiagnostic> Diagnostics);
public sealed record StudioCampaignPublishResult(
    bool Ok,
    bool Conflict,
    string Status,
    string? Error,
    StudioGuildDocument? Guild,
    StudioCampaignDocument? Campaign,
    QuestPackPublishReceipt? Receipt,
    string? ContentHash,
    IReadOnlyList<string> ExperienceIds,
    StudioCampaignCompilationReceipt? CompilationReceipt,
    IReadOnlyList<ComfyQuestContracts.ContractDiagnostic> Diagnostics)
{
    public StudioCampaignPlayReceipt? PlayReceipt { get; init; }
}

public sealed class StudioCampaignCompilationReceipt
{
    public string ReceiptId { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
    public string GuildId { get; set; } = string.Empty;
    public int GuildRevision { get; set; }
    public string CampaignId { get; set; } = string.Empty;
    public int CampaignRevision { get; set; }
    public string PackId { get; set; } = string.Empty;
    public string PackVersion { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public List<StudioCampaignLineageEntry> Experiences { get; set; } = new();
    public string? PublicationStatus { get; set; }
    public string? PackageSha256 { get; set; }
}

public sealed class StudioCampaignLineageEntry
{
    public string ProjectId { get; set; } = string.Empty;
    public int ProjectRevision { get; set; }
    public string ExperienceId { get; set; } = string.Empty;
    public string? AbstractionId { get; set; }
    public int? AbstractionRevision { get; set; }
    public string? AbstractionHash { get; set; }
    public string? ConfigurationHash { get; set; }
    public List<string> SuccessorExperienceIds { get; set; } = new();
}

internal static class StudioCreativeHash
{
    public static string Text(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();
    public static string Parts(params string?[] values) => Text(string.Join("\n--comfy-quest-part--\n", values.Select(value => value ?? string.Empty)));
}
