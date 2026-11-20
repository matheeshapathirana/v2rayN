namespace ServiceLib.Tests.Services;

/// <summary>
/// v2rayN updates itself from the custom fork's GitHub releases
/// (tags "v{Version}-custom.{run}", assets "v2rayN-custom-{target}.zip").
/// </summary>
public class SelfUpdateSourceTests
{
    private const string ForkReleases = "https://github.com/matheeshapathirana/v2rayN/releases";

    [Test]
    public async Task SelfUpdate_ChecksForkReleases()
    {
        var info = CoreInfoManager.Instance.GetCoreInfo(ECoreType.v2rayN)!;

        await info.Url.Should().BeEqualTo(ForkReleases);
        await info.ReleaseApiUrl.Should().BeEqualTo("https://api.github.com/repos/matheeshapathirana/v2rayN/releases");
    }

    [Test]
    public async Task SelfUpdate_DownloadsCustomAssets()
    {
        var info = CoreInfoManager.Instance.GetCoreInfo(ECoreType.v2rayN)!;

        await string.Format(info.DownloadUrlWin64!, "v7.25.6-custom.5")
            .Should().BeEqualTo($"{ForkReleases}/download/v7.25.6-custom.5/v2rayN-custom-windows-64.zip");
        await info.DownloadUrlWinArm64.Should().BeEqualTo($"{ForkReleases}/download/{{0}}/v2rayN-custom-windows-arm64.zip");
        await info.DownloadUrlLinux64.Should().BeEqualTo($"{ForkReleases}/download/{{0}}/v2rayN-custom-linux-64.zip");
        await info.DownloadUrlLinuxArm64.Should().BeEqualTo($"{ForkReleases}/download/{{0}}/v2rayN-custom-linux-arm64.zip");
        await info.DownloadUrlOSX64.Should().BeEqualTo($"{ForkReleases}/download/{{0}}/v2rayN-custom-macos-64.zip");
        await info.DownloadUrlOSXArm64.Should().BeEqualTo($"{ForkReleases}/download/{{0}}/v2rayN-custom-macos-arm64.zip");
    }

    [Test]
    public async Task SelfUpdate_IsOfferedButPatchedXrayIsNot()
    {
        var types = CoreInfoManager.Instance.GetCheckUpdateCoreTypes();
        await types.Should().Contain(ECoreType.v2rayN);
        await types.Should().NotContain(ECoreType.Xray);
    }

    [Test]
    public async Task OtherCores_StillUseTheirOwnRepos()
    {
        await CoreInfoManager.Instance.GetCoreInfo(ECoreType.sing_box)!.Url
            .Should().BeEqualTo("https://github.com/SagerNet/sing-box/releases");
    }

    [Test]
    [Arguments("7.25.6-custom.4", "v7.25.6-custom.5")]
    [Arguments("7.25.6-custom.9", "v7.25.6-custom.10")]
    [Arguments("7.25.6-custom.99", "v7.26.0-custom.100")]
    public async Task NewerCustomBuild_IsOffered(string installed, string releaseTag)
    {
        await (new SemanticVersion(installed) < new SemanticVersion(releaseTag)).Should().BeTrue();
    }

    [Test]
    public async Task SameCustomBuild_IsNotOffered()
    {
        await (new SemanticVersion("7.25.6-custom.5") >= new SemanticVersion("v7.25.6-custom.5")).Should().BeTrue();
    }

    [Test]
    [Arguments("7.25.6-custom.12+3f2a9c1b", "7.25.6-custom.12")]
    [Arguments("7.25.6-custom.12", "7.25.6-custom.12")]
    [Arguments("7.25.6+3f2a9c1b", "7.25.6")]
    public async Task AppVersion_UsesInformationalVersionWithoutBuildMetadata(string informational, string expected)
    {
        await Utils.ResolveAppVersion(informational, new Version(7, 25, 6, 0)).Should().BeEqualTo(expected);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("not-a-version")]
    public async Task AppVersion_FallsBackToAssemblyVersion(string? informational)
    {
        await Utils.ResolveAppVersion(informational, new Version(7, 25, 6, 0)).Should().BeEqualTo("7.25.6");
    }
}
