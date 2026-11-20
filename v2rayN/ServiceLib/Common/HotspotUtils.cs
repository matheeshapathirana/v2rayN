namespace ServiceLib.Common;

/// <summary>
/// Settings helpers for the Wi-Fi hotspot that shares the TUN VPN.
/// </summary>
public static class HotspotUtils
{
    private const int MaxSsidBytes = 32;
    private const int MinPasswordLength = 8;
    private const int MaxPasswordLength = 63;
    private const string PasswordChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    public static string DefaultSsid()
    {
        var name = Environment.MachineName;
        while (name.Length > 1 && Encoding.UTF8.GetByteCount(name) > MaxSsidBytes)
        {
            name = name[..^1];
        }
        return name.IsNullOrEmpty() ? "v2rayN" : name;
    }

    public static string GeneratePassword(int length = 12)
    {
        return RandomNumberGenerator.GetString(PasswordChars, length);
    }

    public static string? ValidateSsid(string? ssid)
    {
        if (ssid.IsNullOrEmpty())
        {
            return ResUI.HotspotSsidInvalid;
        }
        return Encoding.UTF8.GetByteCount(ssid) > MaxSsidBytes ? ResUI.HotspotSsidInvalid : null;
    }

    public static string? ValidatePassword(string? password)
    {
        if (password is null || password.Length < MinPasswordLength || password.Length > MaxPasswordLength)
        {
            return ResUI.HotspotPasswordInvalid;
        }
        return null;
    }

    /// <summary>
    /// Wi-Fi join QR payload, e.g. WIFI:T:WPA;S:Home;P:pass1234;;
    /// </summary>
    public static string WifiQrPayload(string ssid, string password)
    {
        return $"WIFI:T:WPA;S:{EscapeQr(ssid)};P:{EscapeQr(password)};;";
    }

    public static void EnsureDefaults(Config config)
    {
        config.HotspotItem ??= new HotspotItem();
        if (config.HotspotItem.Ssid.IsNullOrEmpty())
        {
            config.HotspotItem.Ssid = DefaultSsid();
        }
        if (config.HotspotItem.Password.IsNullOrEmpty())
        {
            config.HotspotItem.Password = GeneratePassword();
        }
    }

    private static string EscapeQr(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c is '\\' or ';' or ',' or ':' or '"')
            {
                sb.Append('\\');
            }
            sb.Append(c);
        }
        return sb.ToString();
    }
}
