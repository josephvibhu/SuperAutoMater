using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VeloxHub.Services;

namespace VeloxHub;

public partial class MainWindow : Window
{
    private readonly HubService _hubService;
    private readonly ObservableCollection<HubAppItem> _displayedApps = new();
    private string _currentFilter = "All";

    public MainWindow()
    {
        InitializeComponent();
        _hubService = new HubService();

        // Bind items control to our filtered observable collection
        AppsItemsControl.ItemsSource = _displayedApps;
        TxtMachineGuid.Text = _hubService.Config.MachineGuid;

        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyFilter("All");

        if (string.IsNullOrWhiteSpace(_hubService.Config.LicenseKey))
        {
            // First time launch: prompt user to activate depot key
            TxtLicensePill.Text = "Activation Required";
            PillDot.Fill = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Amber
            TxtStatusBanner.Text = "Welcome to Velox Hub. Please enter your organization license key to unlock your suite.";

            ShowLicenseModal(isFirstTime: true);
        }
        else
        {
            await RefreshLicenseStatusAsync();
        }
    }

    private async Task RefreshLicenseStatusAsync()
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
            TxtStatusBanner.Text = _hubService.StatusBanner ?? "License inactive. Please click 'Change Key' to enter credentials.";
        }

        ApplyFilter(_currentFilter);
    }

    private void ApplyFilter(string filter)
    {
        _currentFilter = filter;
        _displayedApps.Clear();

        var query = filter switch
        {
            "Installed" => _hubService.Apps.Where(a => a.State == AppInstallState.Installed),
            _ => _hubService.Apps
        };

        foreach (var app in query)
        {
            _displayedApps.Add(app);
        }

        // Update button visual styles
        if (BtnNavAll != null && BtnNavInstalled != null)
        {
            if (filter == "Installed")
            {
                BtnNavInstalled.Background = new SolidColorBrush(Color.FromRgb(20, 31, 54));
                BtnNavInstalled.Foreground = new SolidColorBrush(Colors.White);
                BtnNavAll.Background = Brushes.Transparent;
                BtnNavAll.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            }
            else
            {
                BtnNavAll.Background = new SolidColorBrush(Color.FromRgb(20, 31, 54));
                BtnNavAll.Foreground = new SolidColorBrush(Colors.White);
                BtnNavInstalled.Background = Brushes.Transparent;
                BtnNavInstalled.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            }
        }
    }

    private void BtnNavAll_Click(object sender, RoutedEventArgs e)
    {
        ApplyFilter("All");
    }

    private void BtnNavInstalled_Click(object sender, RoutedEventArgs e)
    {
        ApplyFilter("Installed");
    }

    private async void BtnNavRefresh_Click(object sender, RoutedEventArgs e)
    {
        _hubService.RefreshInstalledStates();
        await RefreshLicenseStatusAsync();
    }

    private async void BtnAppAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is HubAppItem app)
        {
            await _hubService.ExecuteActionAsync(app);
            ApplyFilter(_currentFilter);
        }
    }

    private void BtnChangeKey_Click(object sender, RoutedEventArgs e)
    {
        ShowLicenseModal(isFirstTime: false);
    }

    private void ShowLicenseModal(bool isFirstTime)
    {
        TxtModalKeyInput.Text = _hubService.Config.LicenseKey;
        TxtModalStatus.Text = "";
        PnlModalSuccess.Visibility = Visibility.Collapsed;
        BtnModalActivate.Content = "Activate Key";
        BtnModalActivate.IsEnabled = true;

        if (isFirstTime)
        {
            TxtModalTitle.Text = "Welcome to Velox Platform Suite";
            TxtModalSubtitle.Text = "Please enter your organization subscription key (e.g. VLX-START-..., VLX-PRO-..., VLX-SOLO-...) to unlock your applications.";
            BtnModalCancel.Visibility = Visibility.Collapsed;
        }
        else
        {
            TxtModalTitle.Text = "Depot License Activation";
            TxtModalSubtitle.Text = "Enter your organization subscription key to unlock entitled applications and bind this workstation to your depot cloud cluster.";
            BtnModalCancel.Visibility = Visibility.Visible;
        }

        LicenseModal.Visibility = Visibility.Visible;
        TxtModalKeyInput.Focus();
        TxtModalKeyInput.SelectAll();
    }

    private void BtnAppUninstall_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is HubAppItem app)
        {
            var res = MessageBox.Show(
                $"Are you sure you want to uninstall {app.Title} from this workstation?\n\nThis will remove the local application binaries and Start Menu shortcut.",
                $"Uninstall {app.Title}",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                _hubService.UninstallApp(app);
                ApplyFilter(_currentFilter);
            }
        }
    }

    private void BtnModalCancel_Click(object sender, RoutedEventArgs e)
    {
        LicenseModal.Visibility = Visibility.Collapsed;
    }

    private async void BtnModalActivate_Click(object sender, RoutedEventArgs e)
    {
        // If already in success mode, button acts as Done
        if (BtnModalActivate.Content?.ToString() == "Done")
        {
            LicenseModal.Visibility = Visibility.Collapsed;
            return;
        }

        var key = TxtModalKeyInput.Text?.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            TxtModalStatus.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            TxtModalStatus.Text = "Please enter an organization license key.";
            return;
        }

        TxtModalStatus.Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11));
        TxtModalStatus.Text = "Connecting to Velox Cloud licensing cluster...";
        BtnModalActivate.IsEnabled = false;

        var success = await _hubService.ValidateLicenseAsync(key);

        if (success && _hubService.CurrentLicense != null)
        {
            var lic = _hubService.CurrentLicense;
            TxtModalStatus.Text = "";
            PnlModalSuccess.Visibility = Visibility.Visible;
            TxtSuccessCompany.Text = lic.CompanyName;
            TxtSuccessTier.Text = $" • {lic.PlanTier} Plan";
            TxtSuccessQuota.Text = $"Monthly Quota: {lic.DevicesUsedMonth}/{lic.MaxDevicesMonth} laptops tested";
            TxtSuccessSeats.Text = $"Workstation Seats: {lic.WorkstationSeatsUsed} of {lic.MaxWorkstationSeats} active • {lic.DaysRemaining} days remaining";

            BtnModalActivate.Content = "Done";
            BtnModalActivate.IsEnabled = true;
            BtnModalCancel.Visibility = Visibility.Collapsed;

            await RefreshLicenseStatusAsync();
        }
        else
        {
            TxtModalStatus.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            TxtModalStatus.Text = _hubService.StatusBanner ?? "License validation failed. Check the key format or active subscription.";
            BtnModalActivate.IsEnabled = true;
        }
    }
}