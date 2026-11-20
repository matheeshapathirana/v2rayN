namespace ServiceLib.ViewModels;

public partial class HotspotViewModel : MyReactiveObject
{
    private readonly HotspotCoordinator _coordinator;
    private readonly Config _appConfig;
    private readonly Func<Config, Task> _saveConfig;
    private string? _lastError;

    [Reactive]
    public partial string Ssid { get; set; }

    [Reactive]
    public partial string Password { get; set; }

    [Reactive]
    public partial string StatusText { get; set; }

    [Reactive]
    public partial int ClientCount { get; set; }

    [Reactive]
    public partial bool IsOn { get; set; }

    [Reactive]
    public partial string QrPayload { get; set; }

    public ReactiveCommand<RxVoid, RxVoid> StartCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> StopCmd { get; }

    public HotspotViewModel()
        : this(HotspotCoordinator.Instance, AppManager.Instance.Config, async c => await ConfigHandler.SaveConfig(c))
    {
    }

    public HotspotViewModel(HotspotCoordinator coordinator, Config config, Func<Config, Task> saveConfig)
    {
        _coordinator = coordinator;
        _appConfig = config;
        _saveConfig = saveConfig;

        HotspotUtils.EnsureDefaults(_appConfig);
        Ssid = _appConfig.HotspotItem.Ssid;
        Password = _appConfig.HotspotItem.Password;
        StatusText = ResUI.HotspotStatusOff;

        this.WhenAnyValue(x => x.Ssid, x => x.Password)
            .Subscribe(t => QrPayload = HotspotUtils.WifiQrPayload(t.Item1 ?? string.Empty, t.Item2 ?? string.Empty));

        StartCmd = ReactiveCommand.CreateFromTask(StartAsync);
        StopCmd = ReactiveCommand.CreateFromTask(StopAsync);

        UpdateFromState();
    }

    public async Task StartAsync()
    {
        var invalid = HotspotUtils.ValidateSsid(Ssid) ?? HotspotUtils.ValidatePassword(Password);
        if (invalid != null)
        {
            _lastError = invalid;
            StatusText = invalid;
            return;
        }

        _appConfig.HotspotItem.Ssid = Ssid;
        _appConfig.HotspotItem.Password = Password;
        await _saveConfig(_appConfig);

        _lastError = null;
        StatusText = ResUI.HotspotStatusStarting;
        _lastError = await _coordinator.StartAsync();
        UpdateFromState();
    }

    public async Task StopAsync()
    {
        _lastError = null;
        await _coordinator.StopAsync();
        UpdateFromState();
    }

    public Task RefreshAsync()
    {
        UpdateFromState();
        return Task.CompletedTask;
    }

    private void UpdateFromState()
    {
        var state = _coordinator.State;
        IsOn = state == EHotspotState.On;
        ClientCount = IsOn ? _coordinator.ClientCount : 0;

        if (state == EHotspotState.On)
        {
            _lastError = null;
        }
        StatusText = _lastError ?? state switch
        {
            EHotspotState.On => ResUI.HotspotStatusOn,
            EHotspotState.Starting => ResUI.HotspotStatusStarting,
            EHotspotState.Unavailable => ResUI.HotspotUnavailable,
            _ => ResUI.HotspotStatusOff,
        };
    }
}
