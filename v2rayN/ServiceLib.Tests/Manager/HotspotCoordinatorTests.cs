namespace ServiceLib.Tests.Manager;

public class HotspotCoordinatorTests
{
    private sealed class FakeHotspotController : IHotspotController
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

    private sealed class Harness
    {
        public FakeHotspotController Fake { get; } = new();
        public Config Config { get; } = new()
        {
            TunModeItem = new TunModeItem { EnableTun = true },
            HotspotItem = new HotspotItem { Ssid = "Home", Password = "pass1234" },
        };
        public bool CoreRunning { get; set; } = true;
        public List<string> Notices { get; } = [];
        public HotspotCoordinator Coordinator { get; }

        public Harness(bool withController = true)
        {
            Coordinator = new HotspotCoordinator(
                () => withController ? Fake : null,
                () => Config,
                () => CoreRunning,
                Notices.Add,
                delay: _ => Task.CompletedTask,
                enableGuardLoop: false);
        }
    }

    [Test]
    public async Task Start_TunDisabled_ReturnsNeedTun_AndDoesNotStart()
    {
        var h = new Harness();
        h.Config.TunModeItem.EnableTun = false;

        var result = await h.Coordinator.StartAsync();

        await result.Should().BeEqualTo(ResUI.HotspotNeedTun);
        await h.Fake.StartCalls.Should().BeEqualTo(0);
    }

    [Test]
    public async Task Start_CoreNotRunning_ReturnsNeedTun()
    {
        var h = new Harness { CoreRunning = false };

        var result = await h.Coordinator.StartAsync();

        await result.Should().BeEqualTo(ResUI.HotspotNeedTun);
        await h.Fake.StartCalls.Should().BeEqualTo(0);
    }

    [Test]
    public async Task Start_InternetNotOnTun_ReturnsNotOnTun()
    {
        var h = new Harness();
        h.Fake.InternetOnTun = false;

        var result = await h.Coordinator.StartAsync();

        await result.Should().BeEqualTo(ResUI.HotspotNotOnTun);
        await h.Fake.StartCalls.Should().BeEqualTo(0);
    }

    [Test]
    public async Task Start_InvalidPassword_ReturnsValidationError()
    {
        var h = new Harness();
        h.Config.HotspotItem.Password = "short";

        var result = await h.Coordinator.StartAsync();

        await result.Should().BeEqualTo(ResUI.HotspotPasswordInvalid);
        await h.Fake.StartCalls.Should().BeEqualTo(0);
    }

    [Test]
    public async Task Start_InvalidSsid_ReturnsValidationError()
    {
        var h = new Harness();
        h.Config.HotspotItem.Ssid = "";

        var result = await h.Coordinator.StartAsync();

        await result.Should().BeEqualTo(ResUI.HotspotSsidInvalid);
        await h.Fake.StartCalls.Should().BeEqualTo(0);
    }

    [Test]
    public async Task Start_AllGood_StartsWithConfigValues()
    {
        var h = new Harness();

        var result = await h.Coordinator.StartAsync();

        await result.Should().BeNull();
        await h.Fake.LastStart.Should().BeEqualTo(("Home", "pass1234"));
        await h.Coordinator.State.Should().BeEqualTo(EHotspotState.On);
    }

    [Test]
    public async Task Start_ControllerError_IsReturned()
    {
        var h = new Harness();
        h.Fake.StartResult = "radio off";

        await (await h.Coordinator.StartAsync()).Should().BeEqualTo("radio off");
    }

    [Test]
    public async Task BeforeCoreStop_WhenOn_StopsAndRemembers_ThenAfterCoreStart_TunActive_Restarts()
    {
        var h = new Harness();
        await h.Coordinator.StartAsync();

        await h.Coordinator.BeforeCoreStopAsync();
        await h.Fake.StopCalls.Should().BeEqualTo(1);
        await h.Fake.State.Should().BeEqualTo(EHotspotState.Off);

        await h.Coordinator.AfterCoreStartAsync(tunActive: true);
        await h.Fake.StartCalls.Should().BeEqualTo(2);
        await h.Fake.State.Should().BeEqualTo(EHotspotState.On);
    }

    [Test]
    public async Task AfterCoreStart_TunDisabled_DoesNotRestartAndNotifies()
    {
        var h = new Harness();
        await h.Coordinator.StartAsync();
        await h.Coordinator.BeforeCoreStopAsync();
        h.Config.TunModeItem.EnableTun = false;

        await h.Coordinator.AfterCoreStartAsync(tunActive: false);

        await h.Fake.StartCalls.Should().BeEqualTo(1);
        await h.Notices.Should().Contain(ResUI.HotspotStoppedTunOff);
    }

    [Test]
    public async Task AfterCoreStart_RestartFails_NotifiesError()
    {
        var h = new Harness();
        await h.Coordinator.StartAsync();
        await h.Coordinator.BeforeCoreStopAsync();
        h.Fake.InternetOnTun = false;

        await h.Coordinator.AfterCoreStartAsync(tunActive: true);

        await h.Fake.StartCalls.Should().BeEqualTo(1);
        await h.Notices.Should().Contain(ResUI.HotspotNotOnTun);
    }

    [Test]
    public async Task AfterCoreStart_WaitsForTunToBecomeInternetProfile()
    {
        var h = new Harness();
        await h.Coordinator.StartAsync();
        await h.Coordinator.BeforeCoreStopAsync();
        h.Fake.TunReadyAfterChecks = h.Fake.TunChecks + 3;

        await h.Coordinator.AfterCoreStartAsync(tunActive: true);

        await h.Fake.StartCalls.Should().BeEqualTo(2);
        await h.Notices.Should().BeEmpty();
    }

    [Test]
    public async Task UserStop_ThenCoreRestart_DoesNotRestart()
    {
        var h = new Harness();
        await h.Coordinator.StartAsync();
        await h.Coordinator.StopAsync();

        await h.Coordinator.BeforeCoreStopAsync();
        await h.Coordinator.AfterCoreStartAsync(tunActive: true);

        await h.Fake.StartCalls.Should().BeEqualTo(1);
    }

    [Test]
    public async Task BeforeCoreStop_WhenOff_DoesNothing()
    {
        var h = new Harness();

        await h.Coordinator.BeforeCoreStopAsync();
        await h.Coordinator.AfterCoreStartAsync(tunActive: true);

        await h.Fake.StopCalls.Should().BeEqualTo(0);
        await h.Fake.StartCalls.Should().BeEqualTo(0);
    }

    [Test]
    public async Task Guard_WhenNotOnTun_StopsAndNotifies()
    {
        var h = new Harness();
        await h.Coordinator.StartAsync();
        h.Fake.InternetOnTun = false;

        await h.Coordinator.CheckGuardAsync();

        await h.Fake.StopCalls.Should().BeEqualTo(1);
        await h.Notices.Should().Contain(ResUI.HotspotStoppedVpnDown);
    }

    [Test]
    public async Task Guard_WhenOnTun_KeepsRunning()
    {
        var h = new Harness();
        await h.Coordinator.StartAsync();

        await h.Coordinator.CheckGuardAsync();

        await h.Fake.StopCalls.Should().BeEqualTo(0);
        await h.Notices.Should().BeEmpty();
    }

    [Test]
    public async Task Guard_WhenOff_DoesNothing()
    {
        var h = new Harness();
        h.Fake.InternetOnTun = false;

        await h.Coordinator.CheckGuardAsync();

        await h.Fake.StopCalls.Should().BeEqualTo(0);
        await h.Notices.Should().BeEmpty();
    }

    [Test]
    public async Task NullController_AllCallsAreNoOps()
    {
        var h = new Harness(withController: false);

        await h.Coordinator.IsAvailable.Should().BeFalse();
        await h.Coordinator.State.Should().BeEqualTo(EHotspotState.Unavailable);
        await (await h.Coordinator.StartAsync()).Should().NotBeNull();
        await h.Coordinator.StopAsync();
        await h.Coordinator.BeforeCoreStopAsync();
        await h.Coordinator.AfterCoreStartAsync(true);
        await h.Coordinator.CheckGuardAsync();
        await h.Notices.Should().BeEmpty();
    }
}
