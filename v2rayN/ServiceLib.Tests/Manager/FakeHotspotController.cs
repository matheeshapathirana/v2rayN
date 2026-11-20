namespace ServiceLib.Tests.Manager;

internal sealed class FakeHotspotController : IHotspotController
{
    public EHotspotState State { get; set; } = EHotspotState.Off;
    public int ClientCount { get; set; }
    public bool InternetOnTun { get; set; } = true;
    public string? StartResult { get; set; }
    /// <summary>State left behind when StartAsync reports an error (Windows may still be bringing it up).</summary>
    public EHotspotState StateAfterFailedStart { get; set; } = EHotspotState.Off;
    public bool StopSucceeds { get; set; } = true;
    /// <summary>When set, StartAsync waits for this before completing.</summary>
    public TaskCompletionSource? StartBlocker { get; set; }
    public int StartCalls { get; private set; }
    public int StopCalls { get; private set; }
    public (string Ssid, string Password)? LastStart { get; private set; }

    public int TunReadyAfterChecks { get; set; }
    public int TunChecks { get; private set; }

    public Task<bool> IsInternetOnTunAsync()
    {
        TunChecks++;
        return Task.FromResult(InternetOnTun && TunChecks > TunReadyAfterChecks);
    }

    public async Task<string?> StartAsync(string ssid, string password)
    {
        StartCalls++;
        LastStart = (ssid, password);
        if (StartBlocker != null)
        {
            await StartBlocker.Task;
        }
        State = StartResult == null ? EHotspotState.On : StateAfterFailedStart;
        return StartResult;
    }

    public Task<bool> StopAsync()
    {
        StopCalls++;
        if (StopSucceeds)
        {
            State = EHotspotState.Off;
        }
        return Task.FromResult(StopSucceeds);
    }
}
