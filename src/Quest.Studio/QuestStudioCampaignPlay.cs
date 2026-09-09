using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Comfy.Quest.Studio;

public sealed class StudioCampaignPlayReceipt
{
    public string Schema { get; set; } = "comfy-quest-studio-campaign-play/v1";
    public string OperationId { get; set; } = string.Empty;
    public string State { get; set; } = "pending";
    public string? FailedStage { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }
    public string GuildId { get; set; } = string.Empty;
    public string CampaignId { get; set; } = string.Empty;
    public int CampaignRevision { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public string CreatorSessionId { get; set; } = string.Empty;
    public string Machine { get; set; } = string.Empty;
    public string WorldUid { get; set; } = string.Empty;
    public string FixtureRequestId { get; set; } = string.Empty;
    public string FixturePreparationId { get; set; } = string.Empty;
    public string FixtureReceiptPath { get; set; } = string.Empty;
    public string FixtureReceiptSha256 { get; set; } = string.Empty;
    public string FixtureProofLevel { get; set; } = string.Empty;
    public string FixtureDisclaimer { get; set; } = string.Empty;
    public List<StudioCampaignFixtureTarget> FixtureTargets { get; set; } = new();
    public string BindingAnchorZdo { get; set; } = string.Empty;
    public string FirstProjectId { get; set; } = string.Empty;
    public string FirstExperienceId { get; set; } = string.Empty;
    public string? PackageSha256 { get; set; }
    public string? ActivationId { get; set; }
    public string? CandidateRequestId { get; set; }
    public string? BindRequestId { get; set; }
    public string? BindingChangeId { get; set; }
    public string? BindingInstanceId { get; set; }
    public List<StudioCampaignPlayStage> Stages { get; set; } = new();
    public List<string> Limitations { get; set; } = new();
}

public sealed class StudioCampaignFixtureTarget
{
    public string Role { get; set; } = string.Empty;
    public string MatcherTarget { get; set; } = string.Empty;
    public string ZdoId { get; set; } = string.Empty;
}

public sealed class StudioCampaignPlayStage
{
    public string Stage { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTimeOffset AtUtc { get; set; }
    public string? EvidenceId { get; set; }
    public string? Detail { get; set; }
}

internal sealed record StudioCampaignPlayPrerequisites(
    string OperationId,
    string CreatorSessionId,
    bool ResumedRunningSession,
    string Machine,
    string WorldUid,
    string FixtureRequestId,
    string FixturePreparationId,
    string FixtureReceiptPath,
    string FixtureReceiptSha256,
    string FixtureProofLevel,
    string FixtureDisclaimer,
    IReadOnlyList<StudioCampaignFixtureTarget> FixtureTargets,
    string BindingAnchorZdo);

internal sealed record StudioCampaignPlayPrerequisiteResult(
    bool Ok,
    string? Error,
    StudioCampaignPlayPrerequisites? Value)
{
    public static StudioCampaignPlayPrerequisiteResult Fail(string error) => new(false, error, null);
    public static StudioCampaignPlayPrerequisiteResult Success(StudioCampaignPlayPrerequisites value) => new(true, null, value);
}

internal interface IStudioCampaignPlayPrerequisiteRunner
{
    Task<StudioCampaignPlayPrerequisiteResult> EnsureAsync(string operationId, CancellationToken cancellationToken);
}

internal sealed class StudioCampaignPlayPrerequisiteRunner : IStudioCampaignPlayPrerequisiteRunner
{
    const int MaxOutputChars = 1024 * 1024;
    readonly IQuestStudioHost _host;

    public StudioCampaignPlayPrerequisiteRunner(IQuestStudioHost host) => _host = host;

    public async Task<StudioCampaignPlayPrerequisiteResult> EnsureAsync(string operationId, CancellationToken cancellationToken)
    {
        var linux = !OperatingSystem.IsWindows();
        var repositoryRoot = linux ? AppContext.BaseDirectory : (_host as IQuestStudioRAndDHost)?.RepositoryRoot;
        if (string.IsNullOrWhiteSpace(repositoryRoot))
            return StudioCampaignPlayPrerequisiteResult.Fail("campaign_play_repository_unavailable");
        repositoryRoot = Path.GetFullPath(repositoryRoot);
        var script = Path.GetFullPath(linux
            ? Path.Combine(repositoryRoot, "campaign", "campaign_play_prerequisites.py")
            : Path.Combine(repositoryRoot, "tools", "quest-studio", "Invoke-CampaignPlayPrerequisites.ps1"));
        if (!script.StartsWith(repositoryRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) || !File.Exists(script))
            return StudioCampaignPlayPrerequisiteResult.Fail("campaign_play_tool_unavailable");
        var valheim = _host.FindValheim();
        if (string.IsNullOrWhiteSpace(valheim))
            return StudioCampaignPlayPrerequisiteResult.Fail("valheim_not_found");

        var start = new ProcessStartInfo
        {
            FileName = linux ? "python3" : "powershell.exe",
            WorkingDirectory = repositoryRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        var operationRoot = Path.GetFullPath(Path.Combine(linux ? _host.StateDirectory : repositoryRoot,
            "captures", "campaign-play", operationId));
        foreach (var argument in linux ? new[] { script, "--operation-id", operationId,
            "--valheim-root", Path.GetFullPath(valheim), "--output", operationRoot } : new[]
                 {
                     "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script,
                     "-OperationId", operationId, "-ValheimRoot", Path.GetFullPath(valheim),
                 })
            start.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) return StudioCampaignPlayPrerequisiteResult.Fail("campaign_play_tool_start_failed");
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            try { await process.WaitForExitAsync(cancellationToken); }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw;
            }
            var output = await outputTask;
            var error = await errorTask;
            if (output.Length > MaxOutputChars || error.Length > MaxOutputChars)
                return StudioCampaignPlayPrerequisiteResult.Fail("campaign_play_tool_output_too_large");
            if (process.ExitCode != 0)
                return StudioCampaignPlayPrerequisiteResult.Fail(
                    "campaign_play_prerequisites_failed:" + BoundedDetail(error.Length > 0 ? error : output));
            var parsed = Parse(operationId, output);
            if (!parsed.Ok || parsed.Value is null) return parsed;
            var fixturePath = Path.GetFullPath(parsed.Value.FixtureReceiptPath);
            var prefix = operationRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var info = new FileInfo(fixturePath);
            if (!fixturePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || !info.Exists || info.Length is <= 0 or > 1024 * 1024)
                return StudioCampaignPlayPrerequisiteResult.Fail("campaign_play_fixture_receipt_unavailable");
            using var fixtureStream = File.OpenRead(fixturePath);
            var actualHash = Convert.ToHexString(SHA256.HashData(fixtureStream)).ToLowerInvariant();
            return string.Equals(actualHash, parsed.Value.FixtureReceiptSha256, StringComparison.OrdinalIgnoreCase)
                ? parsed
                : StudioCampaignPlayPrerequisiteResult.Fail("campaign_play_fixture_receipt_hash_mismatch");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            return StudioCampaignPlayPrerequisiteResult.Fail("campaign_play_tool_failed:" + exception.GetType().Name);
        }
    }

    internal static StudioCampaignPlayPrerequisiteResult Parse(string operationId, string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.GetProperty("schema").GetString() != "comfy-quest-studio-campaign-play-prerequisites/v1"
                || root.GetProperty("operation_id").GetString() != operationId
                || root.GetProperty("state").GetString() != "ready")
                return StudioCampaignPlayPrerequisiteResult.Fail("campaign_play_prerequisites_invalid");
            var fixture = root.GetProperty("fixture");
            var anchor = fixture.GetProperty("binding_anchor");
            var zdo = anchor.GetProperty("zdo_id").GetString() ?? string.Empty;
            var creatorSessionId = root.GetProperty("creator_session_id").GetString() ?? string.Empty;
            var machine = root.GetProperty("machine").GetString() ?? string.Empty;
            var worldUid = root.GetProperty("world_uid").GetString() ?? string.Empty;
            var fixtureRequestId = root.GetProperty("fixture_request_id").GetString() ?? string.Empty;
            var fixtureReceiptPath = root.GetProperty("fixture_receipt_path").GetString() ?? string.Empty;
            var fixtureReceiptSha256 = root.GetProperty("fixture_receipt_sha256").GetString() ?? string.Empty;
            var preparationId = fixture.GetProperty("preparation_id").GetString() ?? string.Empty;
            var objects = fixture.GetProperty("objects");
            var targets = fixture.GetProperty("targets").EnumerateArray().ToArray();
            if (!SafeToken(creatorSessionId, 80) || !SafeToken(machine, 80)
                || worldUid != "-7600395338659582326" || !SafeToken(fixtureRequestId, 96)
                || string.IsNullOrWhiteSpace(fixtureReceiptPath)
                || !SafeHash(fixtureReceiptSha256) || !SafeToken(preparationId, 96)
                || fixture.GetProperty("schema").GetString() != "comfy-questlab-signature-hunt-fixture/v2"
                || fixture.GetProperty("fixture_id").GetString() != "slayers-signature-hunt"
                || fixture.GetProperty("fixture_revision").GetInt32() != 4
                || fixture.GetProperty("target_lifecycle").GetString() != "runtime-stage-entry"
                || fixture.GetProperty("state").GetString() != "ready"
                || fixture.GetProperty("proof_level").GetString() != "fixture-preparation"
                || fixture.GetProperty("request_id").GetString() != fixtureRequestId
                || !string.Equals(fixture.GetProperty("machine").GetString(), machine, StringComparison.OrdinalIgnoreCase)
                || fixture.GetProperty("world_name").GetString() != "ComfyQuestDemo"
                || fixture.GetProperty("world_uid").GetString() != worldUid
                || objects.GetProperty("expected").GetInt32() != 3
                || objects.GetProperty("standing_at_capture").GetInt32() != 3
                || targets.Length != 0
                || anchor.GetProperty("role").GetString() != "marker-loadout-sign"
                || anchor.GetProperty("target_kind").GetString() != "sign"
                || !SafeZdo(zdo))
                return StudioCampaignPlayPrerequisiteResult.Fail("campaign_play_fixture_evidence_invalid");
            return StudioCampaignPlayPrerequisiteResult.Success(new(
                operationId,
                creatorSessionId,
                root.GetProperty("resumed_running_session").GetBoolean(),
                machine,
                worldUid,
                fixtureRequestId,
                preparationId,
                fixtureReceiptPath,
                fixtureReceiptSha256,
                fixture.GetProperty("proof_level").GetString() ?? string.Empty,
                fixture.GetProperty("disclaimer").GetString() ?? string.Empty,
                targets.Select(target => new StudioCampaignFixtureTarget
                {
                    Role = target.GetProperty("role").GetString() ?? string.Empty,
                    MatcherTarget = target.GetProperty("matcher_target").GetString() ?? string.Empty,
                    ZdoId = target.GetProperty("zdo_id").GetString() ?? string.Empty,
                }).ToArray(),
                zdo));
        }
        catch { return StudioCampaignPlayPrerequisiteResult.Fail("campaign_play_prerequisites_unreadable"); }
    }

    static bool SafeZdo(string value)
    {
        var parts = value.Split(':');
        return parts.Length == 2 && long.TryParse(parts[0], out _) && uint.TryParse(parts[1], out _);
    }

    static bool ExactTarget(JsonElement target, string role, string prefab, string matcher)
    {
        var zdo = target.GetProperty("zdo_id").GetString() ?? string.Empty;
        return target.GetProperty("role").GetString() == role
            && target.GetProperty("prefab").GetString() == prefab
            && target.GetProperty("raw_m_name").GetString() == matcher
            && target.GetProperty("matcher_target").GetString() == matcher
            && target.GetProperty("captured_from").GetString() == "Character.m_name"
            && SafeZdo(zdo);
    }

    static bool SafeToken(string value, int maximum) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximum
        && value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.');

    static bool SafeHash(string value) => value.Length == 64 && value.All(char.IsAsciiHexDigit);

    static string BoundedDetail(string value)
    {
        var compact = string.Join(" ", (value ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
        return compact.Length <= 1000 ? compact : compact[..1000];
    }
}
