namespace ServiceLib.Base;

/// <summary>
/// Platform hotspot (Wi-Fi tethering) implementation. Provided by the Windows app; null elsewhere.
/// </summary>
public interface IHotspotController
{
    EHotspotState State { get; }

    int ClientCount { get; }

    /// <summary>True when a TUN adapter (singbox_tun or xray_tun) has a connection profile with internet access.</summary>
    Task<bool> IsInternetOnTunAsync();

    /// <summary>Starts the hotspot. Returns null on success, otherwise an error message.</summary>
    Task<string?> StartAsync(string ssid, string password);

    /// <summary>Stops the hotspot. Returns true once it is confirmed off.</summary>
    Task<bool> StopAsync();
}
