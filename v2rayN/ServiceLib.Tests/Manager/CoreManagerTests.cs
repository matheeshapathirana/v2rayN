using ServiceLib.Tests.CoreConfig;

namespace ServiceLib.Tests.Manager;

public class CoreManagerTests
{
    [Test]
    [NotInParallel] // swaps AppManager's global config and controller; must run alone
    public async Task CoreStop_WhenOwnedHotspotOn_StopsHotspot()
    {
        var fake = new FakeHotspotController { State = EHotspotState.On };
        var previousConfig = AppManager.Instance.Config;
        var config = CoreConfigTestFactory.CreateConfig();
        config.HotspotItem = new HotspotItem { Ssid = "Home", Password = "pass1234", Active = true };
        CoreConfigTestFactory.BindAppManagerConfig(config);
        AppManager.Instance.HotspotController = fake;
        try
        {
            await CoreManager.Instance.CoreStop();

            await fake.StopCalls.Should().BeEqualTo(1);
            await fake.State.Should().BeEqualTo(EHotspotState.Off);
        }
        finally
        {
            AppManager.Instance.HotspotController = null;
            CoreConfigTestFactory.BindAppManagerConfig(previousConfig);
        }
    }

    [Test]
    [Arguments(ECoreType.sing_box)]
    [Arguments(ECoreType.mihomo)]
    [Arguments(ECoreType.Xray)]
    public async Task ShouldRunAsSudo_TunLaunchOnNonWindows_RequiresElevation(ECoreType coreType)
    {
        await CoreManager.ShouldRunAsSudo(isTunLaunch: true, coreType, isNonWindows: true).Should().BeTrue();
    }

    [Test]
    public async Task ShouldRunAsSudo_NonTunLaunch_ShouldNotElevate()
    {
        // Regression guard for the macOS TUN failure: the elevation decision must follow
        // the context snapshot that generated the config. A launch whose snapshot has TUN
        // disabled must never elevate, and a launch whose snapshot has TUN enabled must
        // elevate regardless of later changes to the live config.
        await CoreManager.ShouldRunAsSudo(isTunLaunch: false, ECoreType.sing_box, isNonWindows: true).Should().BeFalse();
        await CoreManager.ShouldRunAsSudo(isTunLaunch: false, ECoreType.Xray, isNonWindows: true).Should().BeFalse();
    }

    [Test]
    public async Task ShouldRunAsSudo_OnWindows_ShouldNotElevate()
    {
        await CoreManager.ShouldRunAsSudo(isTunLaunch: true, ECoreType.sing_box, isNonWindows: false).Should().BeFalse();
    }

    [Test]
    [Arguments(ECoreType.v2fly)]
    [Arguments(ECoreType.hysteria)]
    [Arguments(null)]
    public async Task ShouldRunAsSudo_UnsupportedCoreType_ShouldNotElevate(ECoreType? coreType)
    {
        await CoreManager.ShouldRunAsSudo(isTunLaunch: true, coreType, isNonWindows: true).Should().BeFalse();
    }
}
