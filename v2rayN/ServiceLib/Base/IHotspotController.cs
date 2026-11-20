namespace ServiceLib.Base;

/// <summary>
/// Platform hotspot (Wi-Fi tethering) implementation. Provided by the Windows app; null elsewhere.
/// </summary>
public interface IHotspotController
{
    EHotspotState State { get; }

    int ClientCount { get; }

    /// <summary>True when the OS internet connection profile is the singbox_tun adapter.</summary>
    Task<bool> IsInternetOnTunAsync();

    /// <summary>Starts the hotspot. Returns null on success, otherwise an error message.</summary>
    Task<string?> StartAsync(string ssid, string password);

    Task StopAsync();
}
