namespace v2rayN.Views;

public partial class HotspotWindow
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(3);
    private readonly DispatcherTimer _refreshTimer;
    private bool _syncingPassword;

    public HotspotWindow()
    {
        InitializeComponent();

        _refreshTimer = new DispatcherTimer { Interval = RefreshInterval };
        _refreshTimer.Tick += async (_, _) => await (ViewModel?.RefreshAsync() ?? Task.CompletedTask);
        Closed += (_, _) => _refreshTimer.Stop();

        togShowPassword.Checked += (_, _) => SetPasswordVisible(true);
        togShowPassword.Unchecked += (_, _) => SetPasswordVisible(false);
        txtPasswordHidden.PasswordChanged += (_, _) =>
        {
            if (!_syncingPassword && ViewModel != null)
            {
                ViewModel.Password = txtPasswordHidden.Password;
            }
        };

        this.WhenActivated(disposables =>
        {
            this.Bind(ViewModel, vm => vm.Ssid, v => v.txtSsid.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.Password, v => v.txtPassword.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.StatusText, v => v.txtStatus.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.ClientCount, v => v.txtClients.Text, c => string.Format(ResUI.HotspotClients, c)).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.IsOn, v => v.btnStop.IsEnabled).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.QrPayload, v => v.imgQrcode.Source, QRCodeWindowsUtils.GetQRCode).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.StartCmd, v => v.btnStart).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.StopCmd, v => v.btnStop).DisposeWith(disposables);

            ViewModel.WhenAnyValue(vm => vm.Password)
                .Subscribe(p =>
                {
                    if (txtPasswordHidden.Password != p)
                    {
                        _syncingPassword = true;
                        txtPasswordHidden.Password = p ?? string.Empty;
                        _syncingPassword = false;
                    }
                })
                .DisposeWith(disposables);

            _refreshTimer.Start();
        });
        WindowsUtils.SetDarkBorder(this, AppManager.Instance.Config.UiItem.CurrentTheme);
    }

    private void SetPasswordVisible(bool visible)
    {
        txtPassword.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        txtPasswordHidden.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
    }
}
