namespace ServiceLib.Manager;

/// <summary>
/// Keeps the Wi-Fi hotspot tied to the TUN VPN: it may only run while TUN is up,
/// is paused around core restarts, and is shut off whenever the VPN goes away.
/// </summary>
public sealed class HotspotCoordinator
{
    private static readonly Lazy<HotspotCoordinator> _instance = new(() => new(
        () => AppManager.Instance.HotspotController,
        () => AppManager.Instance.Config,
        () => CoreManager.Instance.IsCoreRunning,
        NoticeManager.Instance.SendMessageAndEnqueue));

    public static HotspotCoordinator Instance => _instance.Value;

    private static readonly TimeSpan GuardInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan TunWaitStep = TimeSpan.FromSeconds(1);
    private const int TunWaitAttempts = 10;
    private const string _tag = "HotspotCoordinator";

    private readonly Func<IHotspotController?> _controller;
    private readonly Func<Config> _config;
    private readonly Func<bool> _isCoreRunning;
    private readonly Action<string> _notify;
    private readonly Func<TimeSpan, Task> _delay;
    private readonly bool _enableGuardLoop;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private bool _resumeAfterCoreStart;
    private CancellationTokenSource? _guardCts;

    public HotspotCoordinator(
        Func<IHotspotController?> controller,
        Func<Config> config,
        Func<bool> isCoreRunning,
        Action<string> notify,
        Func<TimeSpan, Task>? delay = null,
        bool enableGuardLoop = true)
    {
        _controller = controller;
        _config = config;
        _isCoreRunning = isCoreRunning;
        _notify = notify;
        _delay = delay ?? (t => Task.Delay(t));
        _enableGuardLoop = enableGuardLoop;
    }

    public bool IsAvailable => _controller() != null;

    public EHotspotState State => _controller()?.State ?? EHotspotState.Unavailable;

    public int ClientCount => _controller()?.ClientCount ?? 0;

    /// <summary>User-initiated start. Returns null on success, otherwise an error message.</summary>
    public async Task<string?> StartAsync()
    {
        await _gate.WaitAsync();
        try
        {
            return await StartCoreAsync(waitForTun: false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>User-initiated stop; the hotspot will not come back after a core restart.</summary>
    public async Task StopAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _resumeAfterCoreStart = false;
            await StopCoreAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Called before the core stops: pause the hotspot so Windows cannot rebind it to the physical link.</summary>
    public async Task BeforeCoreStopAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_controller()?.State is EHotspotState.On or EHotspotState.Starting)
            {
                _resumeAfterCoreStart = true;
                await StopCoreAsync();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Called after the core started: resume a paused hotspot if TUN is still active.</summary>
    public async Task AfterCoreStartAsync(bool tunActive)
    {
        await _gate.WaitAsync();
        try
        {
            if (!_resumeAfterCoreStart)
            {
                return;
            }
            _resumeAfterCoreStart = false;

            if (!tunActive || !_config().TunModeItem.EnableTun)
            {
                _notify(ResUI.HotspotStoppedTunOff);
                return;
            }

            var error = await StartCoreAsync(waitForTun: true);
            if (error != null)
            {
                _notify(error);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Stops the hotspot if internet traffic is no longer going through singbox_tun.</summary>
    public async Task CheckGuardAsync()
    {
        if (!await _gate.WaitAsync(0))
        {
            return; // a start/stop/restart is in progress
        }
        try
        {
            var controller = _controller();
            if (controller?.State != EHotspotState.On)
            {
                return;
            }
            if (await controller.IsInternetOnTunAsync())
            {
                return;
            }
            _resumeAfterCoreStart = false;
            await StopCoreAsync();
            _notify(ResUI.HotspotStoppedVpnDown);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string?> StartCoreAsync(bool waitForTun)
    {
        var controller = _controller();
        if (controller == null)
        {
            return ResUI.HotspotUnavailable;
        }

        var config = _config();
        if (!config.TunModeItem.EnableTun || !_isCoreRunning())
        {
            return ResUI.HotspotNeedTun;
        }

        var item = config.HotspotItem;
        var invalid = HotspotUtils.ValidateSsid(item?.Ssid) ?? HotspotUtils.ValidatePassword(item?.Password);
        if (invalid != null)
        {
            return invalid;
        }

        if (!await WaitForTunAsync(controller, waitForTun ? TunWaitAttempts : 1))
        {
            return ResUI.HotspotNotOnTun;
        }

        var error = await controller.StartAsync(item!.Ssid, item.Password);
        if (error == null)
        {
            StartGuardLoop();
        }
        return error;
    }

    private async Task<bool> WaitForTunAsync(IHotspotController controller, int attempts)
    {
        for (var i = 0; i < attempts; i++)
        {
            if (await controller.IsInternetOnTunAsync())
            {
                return true;
            }
            if (i < attempts - 1)
            {
                await _delay(TunWaitStep);
            }
        }
        return false;
    }

    private async Task StopCoreAsync()
    {
        StopGuardLoop();
        var controller = _controller();
        if (controller != null && controller.State != EHotspotState.Off)
        {
            await controller.StopAsync();
        }
    }

    private void StartGuardLoop()
    {
        if (!_enableGuardLoop)
        {
            return;
        }
        StopGuardLoop();
        var cts = new CancellationTokenSource();
        _guardCts = cts;
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(GuardInterval);
            try
            {
                while (await timer.WaitForNextTickAsync(cts.Token))
                {
                    await CheckGuardAsync();
                    if (_controller()?.State is null or EHotspotState.Off)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Logging.SaveLog(_tag, ex);
            }
        });
    }

    private void StopGuardLoop()
    {
        _guardCts?.Cancel();
        _guardCts?.Dispose();
        _guardCts = null;
    }
}
