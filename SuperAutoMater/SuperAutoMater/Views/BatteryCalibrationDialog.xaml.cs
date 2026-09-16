using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SuperAutoMater.Wpf.Services;

namespace SuperAutoMater.Wpf.Views
{
    public partial class BatteryCalibrationDialog : Window
    {
        private readonly BatteryCalibrationService _service = BatteryCalibrationService.Instance;
        private bool _isInitialized = false;

        public BatteryCalibrationDialog()
        {
            InitializeComponent();
            _isInitialized = true;
            if (CmbTargetCutoff != null && CmbTargetCutoff.SelectedIndex < 0) CmbTargetCutoff.SelectedIndex = 1;
            if (CmbIntensity != null && CmbIntensity.SelectedIndex < 0) CmbIntensity.SelectedIndex = 0;
            Loaded += BatteryCalibrationDialog_Loaded;
            Closed += BatteryCalibrationDialog_Closed;
            KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape)
                {
                    Close();
                }
            };
        }

        private void BatteryCalibrationDialog_Loaded(object sender, RoutedEventArgs e)
        {
            _service.StatusUpdated += Service_StatusUpdated;
            UpdateUi(_service.GetCurrentStatus());
        }

        private void BatteryCalibrationDialog_Closed(object sender, EventArgs e)
        {
            _service.StatusUpdated -= Service_StatusUpdated;
        }

        private void Service_StatusUpdated(BatteryCalibrationStatus status)
        {
            if (Dispatcher.CheckAccess())
            {
                UpdateUi(status);
            }
            else
            {
                Dispatcher.InvokeAsync(() => UpdateUi(status));
            }
        }

        private void UpdateUi(BatteryCalibrationStatus status)
        {
            if (!_isInitialized || status == null || BtnToggleDrain == null || TxtSocPercent == null) return;

            // 1. AC Status
            if (status.IsAcConnected)
            {
                TxtAcIcon.Text = "🔌 ";
                TxtAcStatus.Text = "AC CHARGER CONNECTED (UNPLUG TO DISCHARGE)";
                TxtAcStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D29922"));
                BorderAcStatus.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D29922"));
            }
            else
            {
                TxtAcIcon.Text = "⚡ ";
                TxtAcStatus.Text = status.IsDraining ? "CONTROLLED DRAIN IN PROGRESS (DISCHARGING)" : "DISCHARGING ON BATTERY";
                TxtAcStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3FB950"));
                BorderAcStatus.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2EA043"));
            }

            // 2. SOC & Gauge
            TxtSocPercent.Text = $"{status.CurrentPercent}%";
            ProgBarSoc.Value = status.CurrentPercent;

            string colorHex = status.CurrentPercent <= status.TargetPercent ? "#F85149" : (status.CurrentPercent <= 20 ? "#D29922" : "#3FB950");
            ProgBarSoc.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
            TxtSocPercent.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
            TxtTargetNotice.Text = $"Target Floor: {status.TargetPercent}% Cutoff · {status.SeriesCells}S1P Pack";

            // 3. Telemetry Matrix
            TxtPackVoltage.Text = $"{status.VoltageVolts:F2} V";
            TxtCellVoltage.Text = $"{status.PerCellVolts:F3} V / Cell";
            TxtDrainWatts.Text = status.DrainRateMw != 0 ? $"{status.DrainRateWatts:F1} W" : "0.0 W";

            if (status.EstimatedSecondsToTarget > 0 && status.CurrentPercent > status.TargetPercent)
            {
                int mins = status.EstimatedSecondsToTarget / 60;
                TxtTimeRemaining.Text = mins >= 60 ? $"~{mins / 60}h {mins % 60}m" : $"~{mins} Mins";
            }
            else if (status.TargetFloorReached)
            {
                TxtTimeRemaining.Text = "0 Mins (Floor Hit)";
            }
            else
            {
                TxtTimeRemaining.Text = "Calculating...";
            }

            // 4. Drain Button Toggle
            if (status.IsDraining)
            {
                BtnToggleDrain.Content = "⏹ STOP DRAIN";
                BtnToggleDrain.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DA3633"));
            }
            else
            {
                BtnToggleDrain.Content = status.TargetFloorReached ? "✓ TARGET REACHED (6%)" : "▶ START CONTROLLED DRAIN";
                BtnToggleDrain.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#238636"));
            }

            // 5. Guided Phase Cards
            UpdatePhaseHighlight(status);

            // 6. Terminal Notice
            TxtStatusHeadline.Text = status.StatusSummary;
            TxtStatusDetail.Text = status.AlertNotice;
            TxtStatusHeadline.Foreground = status.TargetFloorReached
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F85149"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#58A6FF"));
        }

        private void UpdatePhaseHighlight(BatteryCalibrationStatus status)
        {
            var inactiveBorder = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#262C36"));
            var activeBorder = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#388BFD"));
            var doneBorder = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2EA043"));

            CardPhase1.BorderBrush = status.Phase == CalibrationPhase.Phase1_ControlledDrain ? activeBorder : (status.Phase > CalibrationPhase.Phase1_ControlledDrain ? doneBorder : inactiveBorder);
            CardPhase2.BorderBrush = status.Phase == CalibrationPhase.Phase2_RestEquilibrium ? activeBorder : (status.Phase > CalibrationPhase.Phase2_RestEquilibrium ? doneBorder : inactiveBorder);
            CardPhase3.BorderBrush = status.Phase == CalibrationPhase.Phase3_FullCharge ? activeBorder : (status.Phase > CalibrationPhase.Phase3_FullCharge ? doneBorder : inactiveBorder);
            CardPhase4.BorderBrush = status.Phase == CalibrationPhase.Phase4_Completed ? doneBorder : inactiveBorder;

            TxtCurrentPhaseBadge.Text = status.Phase switch
            {
                CalibrationPhase.Phase1_ControlledDrain => "PHASE 1: DRAIN",
                CalibrationPhase.Phase2_RestEquilibrium => "PHASE 2: REST",
                CalibrationPhase.Phase3_FullCharge => "PHASE 3: CHARGE",
                CalibrationPhase.Phase4_Completed => "COMPLETED ✓",
                _ => "PHASE 1"
            };

            TxtPhase1Tag.Text = status.TargetFloorReached ? "DONE ✓" : (status.IsDraining ? "DRAINING" : "READY");
            TxtPhase1Tag.Foreground = status.TargetFloorReached
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3FB950"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#58A6FF"));

            if (status.Phase == CalibrationPhase.Phase2_RestEquilibrium)
            {
                TxtRestTimerDisplay.Visibility = Visibility.Visible;
                int mins = status.RestRemainingSeconds / 60;
                int secs = status.RestRemainingSeconds % 60;
                TxtRestTimerDisplay.Text = $"Rest Countdown: {mins}m {secs:D2}s remaining";
            }
            else
            {
                TxtRestTimerDisplay.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnToggleDrain_Click(object sender, RoutedEventArgs e)
        {
            if (_service.IsDraining)
            {
                _service.StopControlledDrain();
            }
            else
            {
                int target = GetSelectedTargetCutoff();
                DrainIntensity intensity = GetSelectedIntensity();
                _service.StartControlledDrain(target, intensity);
            }
        }

        private int GetSelectedTargetCutoff()
        {
            if (CmbTargetCutoff == null) return 6;
            return CmbTargetCutoff.SelectedIndex switch
            {
                0 => 5,
                1 => 6,
                2 => 7,
                3 => 8,
                4 => 10,
                _ => 6
            };
        }

        private DrainIntensity GetSelectedIntensity()
        {
            if (CmbIntensity == null) return DrainIntensity.Standard;
            return CmbIntensity.SelectedIndex switch
            {
                0 => DrainIntensity.Standard,
                1 => DrainIntensity.Aggressive,
                2 => DrainIntensity.Light,
                _ => DrainIntensity.Standard
            };
        }

        private void CmbTargetCutoff_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || _service == null) return;
            if (!_service.IsDraining)
            {
                UpdateUi(_service.GetCurrentStatus());
            }
        }

        private void CmbIntensity_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || _service == null) return;
            // If running, restart with new intensity
            if (_service.IsDraining)
            {
                int target = GetSelectedTargetCutoff();
                DrainIntensity intensity = GetSelectedIntensity();
                _service.StopControlledDrain();
                _service.StartControlledDrain(target, intensity);
            }
        }

        private void BtnStartRestTimer_Click(object sender, RoutedEventArgs e)
        {
            _service.StartRestPhase(30);
        }

        private void BtnSetPhase3_Click(object sender, RoutedEventArgs e)
        {
            _service.SetPhase(CalibrationPhase.Phase3_FullCharge);
        }

        private void BtnCompleteCalibration_Click(object sender, RoutedEventArgs e)
        {
            _service.SetPhase(CalibrationPhase.Phase4_Completed);
            MessageBox.Show(
                "BMS Gas-Gauge Recalibration Cycle Marked Complete!\n\nBattery fuel-gauge register (Qmax) has been re-indexed. Check updated capacity and wear percentage in SuperAutoMater telemetry.",
                "Recalibration Completed",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void BtnCopySop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string sop = _service.GenerateProtocolSopText();
                Clipboard.SetText(sop);
                MessageBox.Show("Battery Recalibration SOP Instructions copied to clipboard ✓", "SOP Copied", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to copy SOP: " + ex.Message, "Clipboard Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
