namespace ServiceLib.Tests.Common;

public class HotspotUtilsTests
{
    [Test]
    public async Task GeneratePassword_Is12AlphaNumeric()
    {
        var password = HotspotUtils.GeneratePassword();

        await password.Length.Should().BeEqualTo(12);
        await password.All(char.IsAsciiLetterOrDigit).Should().BeTrue();
    }

    [Test]
    public async Task GeneratePassword_IsRandom()
    {
        await (HotspotUtils.GeneratePassword() == HotspotUtils.GeneratePassword()).Should().BeFalse();
    }

    [Test]
    [Arguments("1234567")]
    [Arguments("")]
    [Arguments(null)]
    public async Task ValidatePassword_TooShort_ReturnsError(string? password)
    {
        await HotspotUtils.ValidatePassword(password).Should().BeEqualTo(ResUI.HotspotPasswordInvalid);
    }

    [Test]
    public async Task ValidatePassword_64Chars_ReturnsError()
    {
        await HotspotUtils.ValidatePassword(new string('a', 64)).Should().BeEqualTo(ResUI.HotspotPasswordInvalid);
    }

    [Test]
    [Arguments("12345678")]
    [Arguments("abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijk")]
    public async Task ValidatePassword_8To63Chars_Ok(string password)
    {
        await HotspotUtils.ValidatePassword(password).Should().BeNull();
    }

    [Test]
    public async Task ValidateSsid_33Bytes_ReturnsError()
    {
        await HotspotUtils.ValidateSsid(new string('a', 33)).Should().BeEqualTo(ResUI.HotspotSsidInvalid);
    }

    [Test]
    public async Task ValidateSsid_MultiByteOver32Bytes_ReturnsError()
    {
        // 11 x 3-byte chars = 33 bytes, only 11 characters
        await HotspotUtils.ValidateSsid(new string('中', 11)).Should().BeEqualTo(ResUI.HotspotSsidInvalid);
    }

    [Test]
    [Arguments("")]
    [Arguments(null)]
    public async Task ValidateSsid_Empty_ReturnsError(string? ssid)
    {
        await HotspotUtils.ValidateSsid(ssid).Should().BeEqualTo(ResUI.HotspotSsidInvalid);
    }

    [Test]
    public async Task ValidateSsid_32Bytes_Ok()
    {
        await HotspotUtils.ValidateSsid(new string('a', 32)).Should().BeNull();
    }

    [Test]
    public async Task DefaultSsid_IsValid()
    {
        await HotspotUtils.ValidateSsid(HotspotUtils.DefaultSsid()).Should().BeNull();
    }

    [Test]
    public async Task WifiQrPayload_Plain()
    {
        await HotspotUtils.WifiQrPayload("Home", "pass1234").Should().BeEqualTo("WIFI:T:WPA;S:Home;P:pass1234;;");
    }

    [Test]
    public async Task WifiQrPayload_EscapesSpecialChars()
    {
        await HotspotUtils.WifiQrPayload("a;b,c", "p:q\\r\"s")
            .Should().BeEqualTo("WIFI:T:WPA;S:a\\;b\\,c;P:p\\:q\\\\r\\\"s;;");
    }

    [Test]
    public async Task EnsureDefaults_FillsEmptyValues()
    {
        var config = new Config { HotspotItem = new HotspotItem { Ssid = "", Password = null! } };

        HotspotUtils.EnsureDefaults(config);

        await HotspotUtils.ValidateSsid(config.HotspotItem.Ssid).Should().BeNull();
        await HotspotUtils.ValidatePassword(config.HotspotItem.Password).Should().BeNull();
    }

    [Test]
    public async Task EnsureDefaults_KeepsUserValues()
    {
        var config = new Config { HotspotItem = new HotspotItem { Ssid = "Mine", Password = "secret123" } };

        HotspotUtils.EnsureDefaults(config);

        await config.HotspotItem.Ssid.Should().BeEqualTo("Mine");
        await config.HotspotItem.Password.Should().BeEqualTo("secret123");
    }
}
