using ComfyQuestContracts;

namespace Comfy.Quest.Studio;

internal static class StudioDevChannelConnection
{
    static readonly TimeSpan Freshness = TimeSpan.FromSeconds(3);
    static readonly TimeSpan FutureSkew = TimeSpan.FromSeconds(1);
    static readonly TimeSpan ArmedReconnectGrace = TimeSpan.FromSeconds(30);
    static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    public static bool IsConnected(RuntimeDevChannelStatus? status, DateTimeOffset now) =>
        status is not null
        && status.ObservedUtc <= now.Add(FutureSkew)
        && status.ObservedUtc >= now.Subtract(Freshness);

    public static async Task<RuntimeDevChannelStatus?> WaitForConnectedAsync(
        string runtimeRoot, CancellationToken cancellationToken)
    {
        var store = new RuntimeDevChannelStatusStore(runtimeRoot);
        var status = store.Read();
        if (IsConnected(status, DateTimeOffset.UtcNow)) return status;
        // No heartbeat or an explicitly disarmed heartbeat is actionable immediately. Only an
        // already-armed Runtime earns a grace window: Valheim's main thread can pause heartbeat
        // writes while loading the world without ceasing to be the same creator session.
        if (status?.Armed != true) return null;

        var deadline = DateTimeOffset.UtcNow.Add(ArmedReconnectGrace);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(PollInterval, cancellationToken);
            status = store.Read();
            if (IsConnected(status, DateTimeOffset.UtcNow)) return status;
            if (status?.Armed != true) return null;
        }
        return null;
    }
}
