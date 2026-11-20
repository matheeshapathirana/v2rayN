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
    private static readonly TimeSpan StartRecheckDelay = TimeSpan.FromSeconds(2);
    private const string _tag = "HotspotController";

    private NetworkOperatorTetheringManager? _manager;

    public EHotspotState State
    {
        get
        {
            try
            {
                return (_manager?.TetheringOperationalState) switch
                {
                    TetheringOperationalState.On => EHotspotState.On,
                    TetheringOperationalState.InTransition => EHotspotState.Starting,
                    _ => EHotspotState.Off,
                };
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
                return State == EHotspotState.On ? (int)(_manager?.ClientCount ?? 0) : 0;
            }
            catch
            {
                return 0;
            }
        }
    }

    public Task<bool> IsInternetOnTunAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                var adapterId = NetworkInformation.GetInternetConnectionProfile()?.NetworkAdapter?.NetworkAdapterId;
                if (adapterId is null)
                {
                    return false;
                }
                return NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => TunAdapterNames.Contains(n.Name, StringComparer.OrdinalIgnoreCase))
                    .Any(n => Guid.TryParse(n.Id, out var id) && id == adapterId.Value);
            }
            catch (Exception ex)
            {
                Logging.SaveLog(_tag, ex);
                return false;
            }
        });
    }

    public async Task<string?> StartAsync(string ssid, string password)
    {
        NetworkOperatorTetheringManager? manager = null;
        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile == null)
            {
                return ResUI.HotspotNotOnTun;
            }

            manager = NetworkOperatorTetheringManager.CreateFromConnectionProfile(profile);
            _manager = manager;

            // Stop first so a changed SSID/password takes effect.
            if (manager.TetheringOperationalState == TetheringOperationalState.On)
            {
                await manager.StopTetheringAsync();
            }

            var apConfig = new NetworkOperatorTetheringAccessPointConfiguration
            {
                Ssid = ssid,
                Passphrase = password,
            };
            await manager.ConfigureAccessPointAsync(apConfig);

            var result = await manager.StartTetheringAsync();
            if (result.Status == TetheringOperationStatus.Success)
            {
                return null;
            }

            Logging.SaveLog($"{_tag} StartTethering: {result.Status} {result.AdditionalErrorMessage}");
            return await RecheckStartedAsync(manager)
                ? null
                : result.AdditionalErrorMessage.IsNullOrEmpty() ? result.Status.ToString() : result.AdditionalErrorMessage;
        }
        catch (Exception ex)
        {
            // Seen in testing: StartTetheringAsync throws although the hotspot does come up.
            Logging.SaveLog($"{_tag} StartTethering exception: {ex}");
            if (manager != null && await RecheckStartedAsync(manager))
            {
                return null;
            }
            return ex.Message;
        }
    }

    public async Task StopAsync()
    {
        try
        {
            if (_manager?.TetheringOperationalState is TetheringOperationalState.On or TetheringOperationalState.InTransition)
            {
                await _manager.StopTetheringAsync();
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    private static async Task<bool> RecheckStartedAsync(NetworkOperatorTetheringManager manager)
    {
        await Task.Delay(StartRecheckDelay);
        try
        {
            return manager.TetheringOperationalState == TetheringOperationalState.On;
        }
        catch
        {
            return false;
        }
    }
}
