using System;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SuperAutoMater.Wpf.Controls;
using SuperAutoMater.Wpf.Services;
using VeloxBench.Services;

namespace SuperAutoMater.Wpf.Views
{
    public partial class RetailPrepModalWindow : Window
    {
        private readonly StringBuilder _logBuilder = new StringBuilder();

        public RetailPrepModalWindow()
        {
            InitializeComponent();
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) Close();
            };

            Loaded += async (s, e) =>
            {
                await CheckBitLockerPreFlightAsync();
            };
        }

        private async System.Threading.Tasks.Task CheckBitLockerPreFlightAsync()
        {
            try
            {
                var preFlight = await ProvisioningService.Instance.RunPreFlightChecksAsync("", "", "");
                if (preFlight.BitLockerActive)
                {
                    BorderBitLocker.Visibility = Visibility.Visible;
                    TxtBitLockerWarning.Text = $"BitLocker Active: {preFlight.BitLockerStatus}";
                }
                else
                {
                    BorderBitLocker.Visibility = Visibility.Collapsed;
                }
            }
            catch { }
        }

        private async void BtnSuspendBitLocker_Click(object sender, RoutedEventArgs e)
        {
            BtnSuspendBitLocker.IsEnabled = false;
            AppendLog("[BitLocker] Suspending BitLocker protection on C:...");
            bool ok = await ProvisioningService.Instance.SuspendBitLockerAsync(line => AppendLog(line));
            if (ok)
            {
                BorderBitLocker.Visibility = Visibility.Collapsed;
                VeloxMessageBox.Show("BitLocker protection successfully suspended on drive C:.", "BitLocker Suspended", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                BtnSuspendBitLocker.IsEnabled = true;
                VeloxMessageBox.Show("Failed to suspend BitLocker. Please run as Administrator or check manage-bde output.", "BitLocker Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void BtnFactoryReset_Click(object sender, RoutedEventArgs e)
        {
            var confirm = VeloxMessageBox.Show(
                "LAUNCH FACTORY RESET WIZARD:\n\n" +
                "This will launch the native Windows Factory Reset interface (systemreset -factoryreset) allowing a full cloud or local reinstall.\n\n" +
                "Do you want to launch the reset wizard now?",
                "Confirm Factory Reset",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            AppendLog("[FactoryReset] Launching Windows Factory Reset wizard...");
            bool ok = await ProvisioningService.Instance.LaunchFactoryResetAsync(line => AppendLog(line));
            if (ok)
            {
                AppendLog("✓ Factory reset wizard launched successfully.");
            }
            else
            {
                AppendLog("⚠ Could not launch systemreset.exe. Ensure system is running with administrative privileges.");
            }
        }

        private async void BtnRebootWinRe_Click(object sender, RoutedEventArgs e)
        {
            var confirm = VeloxMessageBox.Show(
                "REBOOT TO WINDOWS RECOVERY ENVIRONMENT (WinRE):\n\n" +
                "The system will configure a one-time boot flag (reagentc /boottore) and schedule an immediate restart into WinRE.\n\n" +
                "Reboot machine now?",
                "Confirm WinRE Reboot",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            AppendLog("[WinRE] Configuring boot flag and scheduling reboot...");
            bool ok = await ProvisioningService.Instance.RebootToWinReAsync(line => AppendLog(line));
            if (ok)
            {
                AppendLog("✓ Machine is restarting into WinRE in 5 seconds...");
            }
            else
            {
                AppendLog("⚠ Failed to configure WinRE reboot. Verify reagentc status.");
            }
        }

        private void AppendLog(string message)
        {
            Dispatcher.Invoke(() =>
            {
                _logBuilder.AppendLine($"[{DateTime.Now:HH:mm:ss}] {message}");
                TxtTerminalLog.Text = _logBuilder.ToString();
                ScrollLogs.ScrollToEnd();
            });
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void BtnExecutePrep_Click(object sender, RoutedEventArgs e)
        {
            bool cleanTemp = ChkCleanTemp.IsChecked == true;
            bool emptyRecycle = ChkEmptyRecycle.IsChecked == true;
            bool clearLogs = ChkClearLogs.IsChecked == true;
            bool resetPower = ChkResetPower.IsChecked == true;
            bool harvestDrivers = ChkHarvestDrivers.IsChecked == true;
            bool triggerOobe = ChkOobeSysprep.IsChecked == true;

            if (triggerOobe)
            {
                var confirm = VeloxMessageBox.Show(
                    "ARM CUSTOMER OOBE CONFIRMATION:\n\n" +
                    "This will arm Windows with the Out-Of-Box Experience (OOBE) setup wizard and shut down the computer for customer packaging.\n\n" +
                    "Are you sure you want to seal this laptop now?",
                    "Confirm Retail Packaging Seal",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (confirm != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            BtnExecutePrep.IsEnabled = false;
            ChkCleanTemp.IsEnabled = false;
            ChkEmptyRecycle.IsEnabled = false;
            ChkClearLogs.IsEnabled = false;
            ChkResetPower.IsEnabled = false;
            ChkHarvestDrivers.IsEnabled = false;
            ChkOobeSysprep.IsEnabled = false;

            _logBuilder.Clear();
            TxtTerminalLog.Text = "";

            EventHandler<RetailPrepProgressEventArgs> handler = (s, args) =>
            {
                Dispatcher.Invoke(() =>
                {
                    PbRetailPrep.Value = args.Percentage;
                    TxtPercent.Text = $"{args.Percentage}%";
                    TxtStatusStage.Text = args.Step;

                    try
                    {
                        var brush = new BrushConverter().ConvertFromString(args.AccentHex) as Brush;
                        if (brush != null) TxtStatusStage.Foreground = brush;
                    }
                    catch { }

                    _logBuilder.AppendLine($"[{DateTime.Now:HH:mm:ss}] [{args.Percentage}%] {args.Step}: {args.Details}");
                    TxtTerminalLog.Text = _logBuilder.ToString();
                    ScrollLogs.ScrollToEnd();
                });
            };

            RetailPrepService.Instance.ProgressUpdated += handler;

            try
            {
                bool success = await RetailPrepService.Instance.RunRetailPrepAsync(
                    cleanTemp,
                    emptyRecycle,
                    clearLogs,
                    resetPower,
                    triggerOobe,
                    harvestDrivers);

                if (success)
                {
                    BtnExecutePrep.Content = "✓ RETAIL PREP COMPLETED";
                    BtnExecutePrep.IsEnabled = true;
                }
            }
            finally
            {
                RetailPrepService.Instance.ProgressUpdated -= handler;
            }
        }
    }
}
