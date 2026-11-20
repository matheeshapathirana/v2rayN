namespace ServiceLib.Tests.Manager;

public class HotspotCoordinatorTests
{
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
        public int SaveCalls { get; private set; }
        public HotspotCoordinator Coordinator { get; }

        public Harness(bool withController = true, HotspotCoordinatorOptions? options = null)
        {
            Coordinator = new HotspotCoordinator(
                () => withController ? Fake : null,
                () => Config,
                () => CoreRunning,
                Notices.Add,
                _ =>
                {
                    SaveCalls++;
                    return Task.CompletedTask;
                },
                options ?? new HotspotCoordinatorOptions { Delay = _ => Task.CompletedTask, GuardInterval = null });
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

    [Test]
    public async Task Start_PersistsOwnership_StopClearsIt()
    {
        var h = new Harness();

        await h.Coordinator.StartAsync();
        await h.Config.HotspotItem.Active.Should().BeTrue();
        await (h.SaveCalls > 0).Should().BeTrue();

        await h.Coordinator.StopAsync();
        await h.Config.HotspotItem.Active.Should().BeFalse();
    }

    [Test]
    public async Task BeforeCoreStop_StopFails_NotifiesKeepsOwnershipAndDoesNotResume()
    {
        var h = new Harness();
        await h.Coordinator.StartAsync();
        h.Fake.StopSucceeds = false;

        await h.Coordinator.BeforeCoreStopAsync();

        await h.Notices.Should().Contain(ResUI.HotspotStopFailed);
        await h.Config.HotspotItem.Active.Should().BeTrue();
        await h.Coordinator.AfterCoreStartAsync(tunActive: false);
        await h.Notices.Contains(ResUI.HotspotStoppedTunOff).Should().BeFalse();
    }

    [Test]
    public async Task Guard_AfterFailedStop_KeepsTryingToStop()
    {
        var h = new Harness();
        await h.Coordinator.StartAsync();
        h.Fake.StopSucceeds = false;
        await h.Coordinator.BeforeCoreStopAsync();
        var stopsSoFar = h.Fake.StopCalls;
        h.Fake.InternetOnTun = false;
        h.Fake.StopSucceeds = true;

        await h.Coordinator.CheckGuardAsync();

        await (h.Fake.StopCalls > stopsSoFar).Should().BeTrue();
        await h.Fake.State.Should().BeEqualTo(EHotspotState.Off);
    }

    [Test]
    public async Task Start_ErrorButStillComingUp_StaysGuarded()
    {
        var h = new Harness();
        h.Fake.StartResult = "timeout";
        h.Fake.StateAfterFailedStart = EHotspotState.Starting;

        await h.Coordinator.StartAsync();
        h.Fake.State = EHotspotState.On;
        h.Fake.InternetOnTun = false;
        await h.Coordinator.CheckGuardAsync();

        await h.Fake.StopCalls.Should().BeEqualTo(1);
        await h.Notices.Should().Contain(ResUI.HotspotStoppedVpnDown);
    }

    [Test]
    public async Task Start_ErrorAndOff_ReleasesOwnership()
    {
        var h = new Harness();
        h.Fake.StartResult = "radio off";

        await h.Coordinator.StartAsync();

        await h.Config.HotspotItem.Active.Should().BeFalse();
    }

    [Test]
    public async Task Guard_SurvivesTransientOff()
    {
        var h = new Harness();
        await h.Coordinator.StartAsync();
        h.Fake.State = EHotspotState.Off;
        await h.Coordinator.CheckGuardAsync();

        h.Fake.State = EHotspotState.On;
        h.Fake.InternetOnTun = false;
        await h.Coordinator.CheckGuardAsync();

        await h.Fake.StopCalls.Should().BeEqualTo(1);
    }

    [Test]
    public async Task Guard_IgnoresHotspotNotStartedByV2rayN()
    {
        var h = new Harness();
        h.Fake.State = EHotspotState.On;
        h.Fake.InternetOnTun = false;

        await h.Coordinator.CheckGuardAsync();
        await h.Coordinator.BeforeCoreStopAsync();

        await h.Fake.StopCalls.Should().BeEqualTo(0);
    }

    [Test]
    public async Task GuardLoop_StopsHotspotWhenVpnDrops()
    {
        var h = new Harness(options: new HotspotCoordinatorOptions
        {
            Delay = _ => Task.CompletedTask,
            GuardInterval = TimeSpan.FromMilliseconds(20),
        });
        await h.Coordinator.StartAsync();

        h.Fake.InternetOnTun = false;
        for (var i = 0; i < 100 && h.Fake.StopCalls == 0; i++)
        {
            await Task.Delay(20);
        }

        await h.Fake.StopCalls.Should().BeEqualTo(1);
    }

    [Test]
    public async Task BeforeCoreStop_DoesNotHangWhenAStartIsStuck()
    {
        var h = new Harness(options: new HotspotCoordinatorOptions
        {
            Delay = _ => Task.CompletedTask,
            GuardInterval = null,
            GateTimeout = TimeSpan.FromMilliseconds(50),
        });
        h.Fake.StartBlocker = new TaskCompletionSource();
        var stuckStart = h.Coordinator.StartAsync();

        var stop = h.Coordinator.BeforeCoreStopAsync();
        var finished = await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(5)));

        await (finished == stop).Should().BeTrue();
        h.Fake.StartBlocker.SetResult();
        await stuckStart;
    }

    [Test]
    public async Task Recover_StopsHotspotLeftByPreviousSession()
    {
        var h = new Harness();
        h.Config.HotspotItem.Active = true;
        h.Fake.State = EHotspotState.On;

        await h.Coordinator.RecoverAsync();

        await h.Fake.StopCalls.Should().BeEqualTo(1);
        await h.Config.HotspotItem.Active.Should().BeFalse();
        await h.Notices.Should().Contain(ResUI.HotspotRecovered);
    }

    [Test]
    public async Task Recover_NotOwned_LeavesUserHotspotAlone()
    {
        var h = new Harness();
        h.Fake.State = EHotspotState.On;

        await h.Coordinator.RecoverAsync();

        await h.Fake.StopCalls.Should().BeEqualTo(0);
    }

    [Test]
    public async Task PreviousSessionHotspot_IsNotResumedAfterFirstCoreStart()
    {
        var h = new Harness();
        h.Config.HotspotItem.Active = true;
        h.Fake.State = EHotspotState.On;

        await h.Coordinator.BeforeCoreStopAsync();
        await h.Coordinator.AfterCoreStartAsync(tunActive: true);

        await h.Fake.StopCalls.Should().BeEqualTo(1);
        await h.Fake.StartCalls.Should().BeEqualTo(0);
    }
}
