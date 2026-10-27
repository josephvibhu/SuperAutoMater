using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using VeloxBench.Services;
using SuperAutoMater.Wpf.ViewModels;
using SuperAutoMater.Wpf.Controls;

namespace VeloxBench.Controls;

public partial class ProvisionWorkspaceControl : UserControl
{
    private PreFlightCheckResult? _preFlight;

    public ProvisionWorkspaceControl()
    {
        InitializeComponent();
        Loaded += ProvisionWorkspaceControl_Loaded;
    }

    private async void ProvisionWorkspaceControl_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshPreFlightAsync();
    }

    private void AppendLog(string message)
    {
        Dispatcher.InvokeAsync(() =>
        {
            string time = DateTime.Now.ToString("HH:mm:ss");
            TxtConsoleLog.AppendText($"[{time}] {message}\n");
            ScrollLog.ScrollToEnd();
        }, DispatcherPriority.Background);
    }

    public async Task RefreshPreFlightAsync()
    {
        AppendLog("Running pre-flight security, power and licensing inspection...");
        BtnRefreshPreflight.IsEnabled = false;

        string mfg = "GenericOEM";
        string model = "Device";
        string serial = "UNKNOWN";

        if (DataContext is MainViewModel vm)
        {
            mfg = vm.Manufacturer;
            model = vm.Model;
            serial = vm.Serial;
        }

        _preFlight = await ProvisioningService.Instance.RunPreFlightChecksAsync(mfg, model, serial);

        // Update BitLocker UI
        TxtBitLockerStatus.Text = _preFlight.BitLockerActive ? "ACTIVE (PROTECTED)" : "OFF / SUSPENDED ✓";
        if (_preFlight.BitLockerActive)
        {
            BorderBitLocker.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D29922"));
            TxtBitLockerStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D29922"));
            BtnSuspendBitLocker.Visibility = Visibility.Visible;
            AppendLog("⚠ WARNING: BitLocker is active on drive C:! Suspend or decrypt prior to customer dispatch.");
        }
        else
        {
            BorderBitLocker.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#238636"));
            TxtBitLockerStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3FB950"));
            BtnSuspendBitLocker.Visibility = Visibility.Collapsed;
            AppendLog("✓ BitLocker is not protecting drive C:. Safe for reset.");
        }

        // Update Power UI
        if (_preFlight.AcPowerConnected)
        {
            TxtPowerStatus.Text = $"AC CONNECTED 🔌 ({_preFlight.BatteryPercent}%)";
            BorderPower.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#238636"));
            TxtPowerStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3FB950"));
        }
        else
        {
            TxtPowerStatus.Text = $"BATTERY {_preFlight.BatteryPercent}% 🔋";
            if (_preFlight.BatteryPercent < 35)
            {
                BorderPower.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F85149"));
                TxtPowerStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F85149"));
                AppendLog("⚠ CAUTION: Battery level is below 35% without AC power connected!");
            }
            else
            {
                BorderPower.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D29922"));
                TxtPowerStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D29922"));
            }
        }

        // Update Activation UI
        TxtActivationStatus.Text = _preFlight.IsWindowsActivated ? "ACTIVATED ✓" : "GRACE / NOT ACTIVATED";
        BorderActivation.BorderBrush = new SolidColorBrush(_preFlight.IsWindowsActivated ? (Color)ColorConverter.ConvertFromString("#238636") : (Color)ColorConverter.ConvertFromString("#30363D"));
        TxtActivationStatus.Foreground = new SolidColorBrush(_preFlight.IsWindowsActivated ? (Color)ColorConverter.ConvertFromString("#3FB950") : (Color)ColorConverter.ConvertFromString("#8B949E"));

        // Default export path
        if (string.IsNullOrWhiteSpace(TxtDriverExportPath.Text))
        {
            TxtDriverExportPath.Text = _preFlight.RecommendedUsbDriverPath;
        }

        AppendLog($"Hardware target: {_preFlight.HardwareModel} | OS: {_preFlight.OsVersion}");
        BtnRefreshPreflight.IsEnabled = true;
    }

    private async void BtnSuspendBitLocker_Click(object sender, RoutedEventArgs e)
    {
        var res = VeloxMessageBox.Show(
            "Are you sure you want to suspend BitLocker protection on C:?\nThis allows offline imaging and factory reset without prompting for recovery keys.",
            "Suspend BitLocker",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (res == MessageBoxResult.Yes)
        {
            bool ok = await ProvisioningService.Instance.SuspendBitLockerAsync(AppendLog);
            if (ok)
            {
                VeloxMessageBox.Show("BitLocker protection has been suspended successfully.", "BitLocker Suspended", MessageBoxButton.OK, MessageBoxImage.Information);
                await RefreshPreFlightAsync();
            }
            else
            {
                VeloxMessageBox.Show("Failed to suspend BitLocker. Check administrative rights.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void BtnRefreshPreflight_Click(object sender, RoutedEventArgs e)
    {
        await RefreshPreFlightAsync();
    }

    private async void BtnExportDrivers_Click(object sender, RoutedEventArgs e)
    {
        string targetPath = TxtDriverExportPath.Text.Trim();
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            VeloxMessageBox.Show("Please specify a target directory for driver export.", "Missing Path", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        BtnExportDrivers.IsEnabled = false;
        ProgBarDrivers.IsIndeterminate = true;
        TxtDriverProgress.Text = "Harvesting 3rd-party drivers via DISM...";

        var progress = new Progress<int>(count =>
        {
            TxtDriverProgress.Text = $"Exporting drivers: {count} packages processed";
        });

        bool success = await ProvisioningService.Instance.ExportDriversAsync(targetPath, AppendLog, progress);

        ProgBarDrivers.IsIndeterminate = false;
        ProgBarDrivers.Value = 100;
        BtnExportDrivers.IsEnabled = true;

        if (success)
        {
            TxtDriverProgress.Text = "Driver export completed successfully ✓";
            VeloxMessageBox.Show($"All 3rd-party drivers successfully harvested and packaged into:\n{targetPath}", "Driver Harvest Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            TxtDriverProgress.Text = "Driver export encountered warnings or errors.";
            VeloxMessageBox.Show("Driver export finished with errors. Review the execution stream below.", "Driver Export Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void BtnInjectDrivers_Click(object sender, RoutedEventArgs e)
    {
        string sourcePath = TxtDriverExportPath.Text.Trim();
        if (!Directory.Exists(sourcePath))
        {
            VeloxMessageBox.Show($"Driver directory not found:\n{sourcePath}", "Path Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = VeloxMessageBox.Show(
            $"Inject all drivers from '{sourcePath}' into the active Windows installation?",
            "Inject Drivers",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm == MessageBoxResult.Yes)
        {
            BtnInjectDrivers.IsEnabled = false;
            bool ok = await ProvisioningService.Instance.InjectDriversAsync(sourcePath, AppendLog);
            BtnInjectDrivers.IsEnabled = true;

            if (ok)
            {
                VeloxMessageBox.Show("Drivers injected into running system successfully.", "Drivers Injected", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                VeloxMessageBox.Show("Driver injection failed. Check administrative permissions.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void BtnSanitize_Click(object sender, RoutedEventArgs e)
    {
        var confirm = VeloxMessageBox.Show(
            "Are you sure you want to sanitize this system?\n\nThis will clear:\n• Windows Event Logs (Application, System, Security)\n• Diagnostic binaries & temporary test caches\n• Prefetch, crash dumps & Recycle Bin\n• Windows 11 BypassNRO flag will be set.",
            "Sanitize System",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm == MessageBoxResult.Yes)
        {
            BtnSanitize.IsEnabled = false;
            bool ok = await ProvisioningService.Instance.SanitizeDepotArtifactsAsync(AppendLog);
            BtnSanitize.IsEnabled = true;

            if (ok)
            {
                VeloxMessageBox.Show("System telemetry and depot artifacts have been purged cleanly.", "Sanitization Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }

    private async void BtnExecuteSysprep_Click(object sender, RoutedEventArgs e)
    {
        bool isShutdown = RbSysprepShutdown.IsChecked == true;
        string actionText = isShutdown ? "GENERALIZE & SHUTDOWN" : "GENERALIZE & REBOOT TO OOBE";

        var confirm = VeloxMessageBox.Show(
            $"WARNING: You are about to execute Windows Sysprep with:\n\nMode: {actionText}\n\nThis will strip unique system SIDs and prepare Windows for customer Out-Of-Box Experience.\n\nAre you sure you want to proceed?",
            "Confirm Sysprep Execution",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        BtnExecuteSysprep.IsEnabled = false;

        string? unattendPath = null;
        if (ChkApplyUnattend.IsChecked == true)
        {
            string sysprepDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "Sysprep");
            unattendPath = Path.Combine(sysprepDir, "unattend.xml");

            var opts = new UnattendOptions
            {
                OemManufacturer = TxtOemBrand.Text.Trim(),
                OemModel = _preFlight?.HardwareModel ?? "Laptop",
                BypassMicrosoftAccount = true,
                SkipEula = true,
                SkipNetworkInOobe = true,
                CreateDefaultUser = true,
                DefaultUserName = TxtDefaultUser.Text.Trim()
            };

            await ProvisioningService.Instance.GenerateUnattendXmlAsync(unattendPath, opts, AppendLog);
        }

        bool ok = await ProvisioningService.Instance.ExecuteSysprepAsync(generalize: true, shutdown: isShutdown, unattendPath: unattendPath, AppendLog);
        BtnExecuteSysprep.IsEnabled = true;

        if (!ok)
        {
            VeloxMessageBox.Show("Sysprep returned an error. Review the execution stream for details.", "Sysprep Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnFactoryReset_Click(object sender, RoutedEventArgs e)
    {
        var confirm = VeloxMessageBox.Show(
            "Launch the native Windows Factory Reset wizard?\nThis will open the Windows Push-Button Reset interface.",
            "Windows Factory Reset",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm == MessageBoxResult.Yes)
        {
            await ProvisioningService.Instance.LaunchFactoryResetAsync(AppendLog);
        }
    }

    private async void BtnWinReReboot_Click(object sender, RoutedEventArgs e)
    {
        var confirm = VeloxMessageBox.Show(
            "Reboot this PC immediately into the Windows Recovery Environment (WinRE)?\n\nThe machine will restart in 5 seconds into recovery options.",
            "Reboot to WinRE",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm == MessageBoxResult.Yes)
        {
            await ProvisioningService.Instance.RebootToWinReAsync(AppendLog);
        }
    }

    private void BtnCopyLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(TxtConsoleLog.Text);
            VeloxMessageBox.Show("Execution log copied to clipboard.", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch { }
    }

    private void BtnClearLog_Click(object sender, RoutedEventArgs e)
    {
        TxtConsoleLog.Clear();
    }
}
