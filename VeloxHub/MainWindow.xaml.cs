using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using VeloxHub.Services;

namespace VeloxHub;

public partial class MainWindow : Window
{
    private readonly HubService _hubService;

    public MainWindow()
    {
        InitializeComponent();
        _hubService = new HubService();
        AppsItemsControl.ItemsSource = _hubService.Apps;
        TxtMachineGuid.Text = _hubService.Config.MachineGuid;

        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshLicenseStatusAsync();
    }

    private async System.Threading.Tasks.Task RefreshLicenseStatusAsync()
    {
        TxtStatusBanner.Text = "Validating depot license with Velox Cloud...";
        var isValid = await _hubService.ValidateLicenseAsync();

        if (isValid && _hubService.CurrentLicense != null)
        {
            var lic = _hubService.CurrentLicense;
            TxtLicensePill.Text = $"{lic.CompanyName} • {lic.PlanTier} Plan";
            PillDot.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Green
            TxtStatusBanner.Text = $"Active Depot License • {lic.DaysRemaining} days remaining • {lic.DevicesUsedMonth}/{lic.MaxDevicesMonth} units tested";
        }
        else
        {
            TxtLicensePill.Text = "No Active License";
            PillDot.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
            TxtStatusBanner.Text = _hubService.StatusBanner ?? "License inactive. Please enter an organization key.";
        }
    }

    private async void BtnAppAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is HubAppItem app)
        {
            await _hubService.ExecuteActionAsync(app);
        }
    }

    private void BtnChangeKey_Click(object sender, RoutedEventArgs e)
    {
        TxtModalKeyInput.Text = _hubService.Config.LicenseKey;
        LicenseModal.Visibility = Visibility.Visible;
        TxtModalKeyInput.Focus();
        TxtModalKeyInput.SelectAll();
    }

    private void BtnModalCancel_Click(object sender, RoutedEventArgs e)
    {
        LicenseModal.Visibility = Visibility.Collapsed;
    }

    private async void BtnModalActivate_Click(object sender, RoutedEventArgs e)
    {
        var key = TxtModalKeyInput.Text?.Trim();
        if (string.IsNullOrWhiteSpace(key)) return;

        LicenseModal.Visibility = Visibility.Collapsed;
        TxtStatusBanner.Text = "Validating new license key...";

        var success = await _hubService.ValidateLicenseAsync(key);
        if (success)
        {
            await RefreshLicenseStatusAsync();
            MessageBox.Show($"Successfully activated license for {_hubService.CurrentLicense.CompanyName} ({_hubService.CurrentLicense.PlanTier} Plan).", "License Verified", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(_hubService.StatusBanner, "License Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        _hubService.RefreshInstalledStates();
        await RefreshLicenseStatusAsync();
    }

    private void BtnFilterInstalled_Click(object sender, RoutedEventArgs e)
    {
        _hubService.RefreshInstalledStates();
    }
}
