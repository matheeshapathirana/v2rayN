using ServiceLib.Tests.Manager;
using ServiceLib.ViewModels;

namespace ServiceLib.Tests.ViewModels;

public class HotspotViewModelTests
{
    private sealed class Harness
    {
        public FakeHotspotController Fake { get; } = new();
        public Config Config { get; } = new()
        {
            TunModeItem = new TunModeItem { EnableTun = true },
            HotspotItem = new HotspotItem { Ssid = "Home", Password = "pass1234" },
        };
        public int SaveCalls { get; private set; }
        public HotspotViewModel Vm { get; }

        public Harness()
        {
            var coordinator = new HotspotCoordinator(
                () => Fake, () => Config, () => true, _ => { },
                delay: _ => Task.CompletedTask, enableGuardLoop: false);
            Vm = new HotspotViewModel(coordinator, Config, _ =>
            {
                SaveCalls++;
                return Task.CompletedTask;
            });
        }
    }

    [Test]
    public async Task LoadsSettingsFromConfig()
    {
        var h = new Harness();

        await h.Vm.Ssid.Should().BeEqualTo("Home");
        await h.Vm.Password.Should().BeEqualTo("pass1234");
        await h.Vm.IsOn.Should().BeFalse();
    }

    [Test]
    public async Task Start_InvalidPassword_ShowsErrorAndDoesNotSave()
    {
        var h = new Harness();
        h.Vm.Password = "short";

        await h.Vm.StartAsync();

        await h.Vm.StatusText.Should().BeEqualTo(ResUI.HotspotPasswordInvalid);
        await h.Config.HotspotItem.Password.Should().BeEqualTo("pass1234");
        await h.SaveCalls.Should().BeEqualTo(0);
        await h.Fake.StartCalls.Should().BeEqualTo(0);
    }

    [Test]
    public async Task Start_Valid_SavesConfigAndStarts()
    {
        var h = new Harness();
        h.Vm.Ssid = "Cafe";
        h.Vm.Password = "newpass99";

        await h.Vm.StartAsync();

        await h.Config.HotspotItem.Ssid.Should().BeEqualTo("Cafe");
        await h.Config.HotspotItem.Password.Should().BeEqualTo("newpass99");
        await h.SaveCalls.Should().BeEqualTo(1);
        await h.Fake.LastStart.Should().BeEqualTo(("Cafe", "newpass99"));
        await h.Vm.IsOn.Should().BeTrue();
        await h.Vm.StatusText.Should().BeEqualTo(ResUI.HotspotStatusOn);
    }

    [Test]
    public async Task Start_CoordinatorError_IsShown()
    {
        var h = new Harness();
        h.Config.TunModeItem.EnableTun = false;

        await h.Vm.StartAsync();

        await h.Vm.StatusText.Should().BeEqualTo(ResUI.HotspotNeedTun);
        await h.Vm.IsOn.Should().BeFalse();
    }

    [Test]
    public async Task Stop_TurnsOff()
    {
        var h = new Harness();
        await h.Vm.StartAsync();

        await h.Vm.StopAsync();

        await h.Fake.StopCalls.Should().BeEqualTo(1);
        await h.Vm.IsOn.Should().BeFalse();
        await h.Vm.StatusText.Should().BeEqualTo(ResUI.HotspotStatusOff);
    }

    [Test]
    public async Task QrPayload_TracksSsidAndPassword()
    {
        var h = new Harness();

        h.Vm.Ssid = "Cafe";

        await h.Vm.QrPayload.Should().BeEqualTo("WIFI:T:WPA;S:Cafe;P:pass1234;;");
    }

    [Test]
    public async Task Refresh_ReadsStateAndClientCount()
    {
        var h = new Harness();
        h.Fake.State = EHotspotState.On;
        h.Fake.ClientCount = 2;

        await h.Vm.RefreshAsync();

        await h.Vm.IsOn.Should().BeTrue();
        await h.Vm.ClientCount.Should().BeEqualTo(2);
    }

    [Test]
    public async Task Refresh_StoppedElsewhere_ShowsOff()
    {
        var h = new Harness();
        await h.Vm.StartAsync();
        h.Fake.State = EHotspotState.Off;

        await h.Vm.RefreshAsync();

        await h.Vm.IsOn.Should().BeFalse();
        await h.Vm.StatusText.Should().BeEqualTo(ResUI.HotspotStatusOff);
    }
}
