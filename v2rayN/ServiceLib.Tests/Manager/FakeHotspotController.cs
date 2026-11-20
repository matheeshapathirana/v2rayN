namespace ServiceLib.Tests.Manager;

internal sealed class FakeHotspotController : IHotspotController
{
    public EHotspotState State { get; set; } = EHotspotState.Off;
    public int ClientCount { get; set; }
    public bool InternetOnTun { get; set; } = true;
    public string? StartResult { get; set; }
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

    public Task<string?> StartAsync(string ssid, string password)
    {
        StartCalls++;
        LastStart = (ssid, password);
        if (StartResult == null)
        {
            State = EHotspotState.On;
        }
        return Task.FromResult(StartResult);
    }

    public Task StopAsync()
    {
        StopCalls++;
        State = EHotspotState.Off;
        return Task.CompletedTask;
    }
}
