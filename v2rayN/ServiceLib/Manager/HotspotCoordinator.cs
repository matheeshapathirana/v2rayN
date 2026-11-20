namespace ServiceLib.Manager;

public sealed record HotspotCoordinatorOptions
{
    /// <summary>Delay used while waiting for singbox_tun to become the internet profile.</summary>
    public Func<TimeSpan, Task> Delay { get; init; } = t => Task.Delay(t);

    /// <summary>How often the guard checks a running hotspot; null disables the guard loop.</summary>
    public TimeSpan? GuardInterval { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Longest a core stop waits for an in-flight hotspot operation before going ahead.</summary>
    public TimeSpan GateTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Keeps the Wi-Fi hotspot tied to the TUN VPN: it may only run while TUN is up,
/// is paused around core restarts, and is shut off whenever the VPN goes away.
/// Only a hotspot v2rayN started ("owned", persisted in HotspotItem.Active) is ever touched
/// automatically; a hotspot the user turned on in Windows Settings is left alone.
/// </summary>
public sealed class HotspotCoordinator
{
    private static readonly Lazy<HotspotCoordinator> _instance = new(() => new(
        () => AppManager.Instance.HotspotController,
        () => AppManager.Instance.Config,
        () => CoreManager.Instance.IsCoreRunning,
        NoticeManager.Instance.SendMessageAndEnqueue,
        async c => await ConfigHandler.SaveConfig(c)));

    public static HotspotCoordinator Instance => _instance.Value;

    private static readonly TimeSpan TunWaitStep = TimeSpan.FromSeconds(1);
    private const int TunWaitAttempts = 10;
    private const string _tag = "HotspotCoordinator";

    private readonly Func<IHotspotController?> _controller;
    private readonly Func<Config> _config;
    private readonly Func<bool> _isCoreRunning;
    private readonly Action<string> _notify;
    private readonly Func<Config, Task> _saveConfig;
    private readonly HotspotCoordinatorOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private bool _startedThisSession;
    private bool _resumeAfterCoreStart;

    public HotspotCoordinator(
        Func<IHotspotController?> controller,
        Func<Config> config,
        Func<bool> isCoreRunning,
        Action<string> notify,
        Func<Config, Task>? saveConfig = null,
        HotspotCoordinatorOptions? options = null)
    {
        _controller = controller;
        _config = config;
        _isCoreRunning = isCoreRunning;
        _notify = notify;
        _saveConfig = saveConfig ?? (_ => Task.CompletedTask);
        _options = options ?? new HotspotCoordinatorOptions();

        if (_options.GuardInterval is { } interval)
        {
            _ = RunGuardLoopAsync(interval);
        }
    }

    public bool IsAvailable => _controller() != null;

    public EHotspotState State => _controller()?.State ?? EHotspotState.Unavailable;

    public int ClientCount => _controller()?.ClientCount ?? 0;

    private bool Owned => _config()?.HotspotItem?.Active == true;

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

    /// <summary>
    /// At app startup: stop a hotspot that a previous (crashed) session left running.
    /// Its TUN adapter is gone, so it can only be sharing the physical connection.
    /// </summary>
    public async Task RecoverAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!Owned)
            {
                return;
            }
            if (_controller()?.State is EHotspotState.Off or null)
            {
                await SetOwnedAsync(false);
                return;
            }
            if (await StopCoreAsync())
            {
                _notify(ResUI.HotspotRecovered);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Called before the core stops: pause the hotspot so Windows cannot rebind it to the physical link.</summary>
    public async Task BeforeCoreStopAsync()
    {
        if (!await _gate.WaitAsync(_options.GateTimeout))
        {
            // A hotspot call is stuck; never block core stop / app exit on it. The guard keeps watching.
            Logging.SaveLog($"{_tag}: BeforeCoreStop timed out waiting for a hotspot operation");
            return;
        }
        try
        {
            if (!Owned)
            {
                return;
            }
            if (_controller()?.State is EHotspotState.Off or null)
            {
                // Ours, but already off (e.g. Windows power saving): release ownership with the VPN session.
                await SetOwnedAsync(false);
                return;
            }
            if (await StopCoreAsync())
            {
                // Only a hotspot started in this session comes back; one left by a crashed session does not.
                _resumeAfterCoreStart = _startedThisSession;
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

    /// <summary>Stops an owned hotspot if internet traffic is no longer going through the TUN adapter.</summary>
    public async Task CheckGuardAsync()
    {
        if (!await _gate.WaitAsync(0))
        {
            return; // a start/stop/restart is in progress
        }
        try
        {
            var controller = _controller();
            if (controller == null || !Owned)
            {
                return;
            }
            if (controller.State is not (EHotspotState.On or EHotspotState.Starting))
            {
                return; // keep ownership: if it is switched back on, it is still guarded
            }
            if (await controller.IsInternetOnTunAsync())
            {
                return;
            }
            _resumeAfterCoreStart = false;
            if (await StopCoreAsync())
            {
                _notify(ResUI.HotspotStoppedVpnDown);
            }
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

        // Take ownership before starting, so a crash mid-start is still recovered and guarded.
        await SetOwnedAsync(true);
        _startedThisSession = true;
        var error = await controller.StartAsync(item!.Ssid, item.Password);
        if (error != null && controller.State == EHotspotState.Off)
        {
            await SetOwnedAsync(false);
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
                await _options.Delay(TunWaitStep);
            }
        }
        return false;
    }

    /// <summary>Stops the hotspot (one retry). Ownership is released only once it is confirmed off.</summary>
    private async Task<bool> StopCoreAsync()
    {
        var controller = _controller();
        if (controller == null)
        {
            return true;
        }
        if (controller.State == EHotspotState.Off)
        {
            await SetOwnedAsync(false);
            return true;
        }

        var stopped = await controller.StopAsync() || await controller.StopAsync();
        if (stopped)
        {
            await SetOwnedAsync(false);
        }
        else
        {
            _notify(ResUI.HotspotStopFailed);
        }
        return stopped;
    }

    private async Task SetOwnedAsync(bool owned)
    {
        var config = _config();
        if (config?.HotspotItem == null || config.HotspotItem.Active == owned)
        {
            return;
        }
        config.HotspotItem.Active = owned;
        try
        {
            await _saveConfig(config);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    private async Task RunGuardLoopAsync(TimeSpan interval)
    {
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync())
        {
            try
            {
                await CheckGuardAsync();
            }
            catch (Exception ex)
            {
                Logging.SaveLog(_tag, ex);
            }
        }
    }
}
