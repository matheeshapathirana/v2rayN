using System.Net.NetworkInformation;
using System.Runtime.Versioning;
using Windows.Networking.Connectivity;
using Windows.Networking.NetworkOperators;

namespace v2rayN.Manager;

/// <summary>
/// Windows Mobile Hotspot via the WinRT tethering API. While TUN is on, Windows treats the
/// TUN adapter as the internet connection, so the hotspot shares the VPN.
/// </summary>
[SupportedOSPlatform("windows10.0.10240.0")]
public sealed class HotspotController : IHotspotController
{
    private static readonly string[] TunAdapterNames = ["singbox_tun", "xray_tun"];
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SettlePoll = TimeSpan.FromMilliseconds(500);
    private const string _tag = "HotspotController";

    private NetworkOperatorTetheringManager? _manager;

    public EHotspotState State
    {
        get
        {
            try
            {
                return Map(GetManager()?.TetheringOperationalState);
            }
            catch (Exception ex)
            {
                Logging.SaveLog(_tag, ex);
                return EHotspotState.Off;
            }
        }
    }

    public int ClientCount
    {
        get
        {
            try
            {
                return State == EHotspotState.On ? (int)(GetManager()?.ClientCount ?? 0) : 0;
            }
            catch
            {
                return 0;
            }
        }
    }

    public Task<bool> IsInternetOnTunAsync()
    {
        return Task.Run(() => FindTunProfile() != null);
    }

    public async Task<string?> StartAsync(string ssid, string password)
    {
        NetworkOperatorTetheringManager? manager = null;
        try
        {
            // Share the TUN profile explicitly: a hotspot created from the physical profile would
            // bypass the VPN. Re-check here, the TUN may have gone since the caller checked it.
            var profile = FindTunProfile();
            if (profile == null)
            {
                return ResUI.HotspotNotOnTun;
            }

            manager = NetworkOperatorTetheringManager.CreateFromConnectionProfile(profile);
            _manager = manager;

            // Stop first so a changed SSID/password takes effect.
            if (manager.TetheringOperationalState != TetheringOperationalState.Off)
            {
                await manager.StopTetheringAsync().AsTask().WaitAsync(CallTimeout);
                await WaitToSettleAsync(manager);
            }

            var apConfig = new NetworkOperatorTetheringAccessPointConfiguration
            {
                Ssid = ssid,
                Passphrase = password,
            };
            await manager.ConfigureAccessPointAsync(apConfig).AsTask().WaitAsync(CallTimeout);

            var result = await manager.StartTetheringAsync().AsTask().WaitAsync(CallTimeout);
            if (result.Status == TetheringOperationStatus.Success)
            {
                return null;
            }

            Logging.SaveLog($"{_tag} StartTethering: {result.Status} {result.AdditionalErrorMessage}");
            if (await WaitToSettleAsync(manager) == TetheringOperationalState.On)
            {
                return null;
            }
            return result.AdditionalErrorMessage.IsNullOrEmpty() ? result.Status.ToString() : result.AdditionalErrorMessage;
        }
        catch (Exception ex)
        {
            // Seen in testing: StartTetheringAsync throws although the hotspot does come up.
            Logging.SaveLog($"{_tag} StartTethering exception: {ex}");
            if (manager != null && await WaitToSettleAsync(manager) == TetheringOperationalState.On)
            {
                return null;
            }
            return ex.Message;
        }
    }

    public async Task<bool> StopAsync()
    {
        var manager = GetManager();
        if (manager == null)
        {
            return true;
        }
        try
        {
            if (manager.TetheringOperationalState != TetheringOperationalState.Off)
            {
                var result = await manager.StopTetheringAsync().AsTask().WaitAsync(CallTimeout);
                if (result.Status != TetheringOperationStatus.Success)
                {
                    Logging.SaveLog($"{_tag} StopTethering: {result.Status} {result.AdditionalErrorMessage}");
                }
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"{_tag} StopTethering exception: {ex}");
        }
        return await WaitToSettleAsync(manager) == TetheringOperationalState.Off;
    }

    /// <summary>
    /// The tethering state is system-wide, so any manager reports it. Create one on demand so a
    /// hotspot this process did not start (e.g. left by a crashed session) is still visible.
    /// </summary>
    private NetworkOperatorTetheringManager? GetManager()
    {
        if (_manager != null)
        {
            return _manager;
        }
        var profile = NetworkInformation.GetInternetConnectionProfile();
        if (profile == null)
        {
            return null;
        }
        _manager = NetworkOperatorTetheringManager.CreateFromConnectionProfile(profile);
        return _manager;
    }

    /// <summary>
    /// The TUN adapter's connection profile once it has internet access. Not necessarily the
    /// OS internet profile: with xray TUN, Windows keeps the physical link as that profile even
    /// though the default route goes through xray_tun.
    /// </summary>
    private static ConnectionProfile? FindTunProfile()
    {
        try
        {
            var tunIds = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => TunAdapterNames.Contains(n.Name, StringComparer.OrdinalIgnoreCase))
                .Select(n => Guid.TryParse(n.Id, out var id) ? id : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .ToHashSet();
            if (tunIds.Count == 0)
            {
                return null;
            }
            return NetworkInformation.GetConnectionProfiles().FirstOrDefault(p =>
                p.NetworkAdapter is { } adapter
                && tunIds.Contains(adapter.NetworkAdapterId)
                && p.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            return null;
        }
    }

    /// <summary>Polls until the state leaves InTransition (or the settle timeout passes) and returns it.</summary>
    private static async Task<TetheringOperationalState> WaitToSettleAsync(NetworkOperatorTetheringManager manager)
    {
        var deadline = DateTime.UtcNow + SettleTimeout;
        while (true)
        {
            TetheringOperationalState state;
            try
            {
                state = manager.TetheringOperationalState;
            }
            catch
            {
                state = TetheringOperationalState.Unknown;
            }
            if (state is TetheringOperationalState.On or TetheringOperationalState.Off || DateTime.UtcNow >= deadline)
            {
                return state;
            }
            await Task.Delay(SettlePoll);
        }
    }

    private static EHotspotState Map(TetheringOperationalState? state) => state switch
    {
        TetheringOperationalState.On => EHotspotState.On,
        TetheringOperationalState.InTransition => EHotspotState.Starting,
        _ => EHotspotState.Off,
    };
}
