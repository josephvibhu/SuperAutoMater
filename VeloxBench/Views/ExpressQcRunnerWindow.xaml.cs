using System;
using System.Collections.Generic;
using System.Media;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SuperAutoMater.Wpf.Services;
using SuperAutoMater.Wpf.ViewModels;

namespace SuperAutoMater.Wpf.Views
{
    /// <summary>
    /// Interaction logic for ExpressQcRunnerWindow.xaml
    /// Autonomous 8-Subsystem Express Hardware Certification HUD
    /// </summary>
    public partial class ExpressQcRunnerWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly Dictionary<string, Brush> _brushCache = new Dictionary<string, Brush>(StringComparer.OrdinalIgnoreCase);

        public ExpressQcFullReport Report { get; private set; }
        public bool CloudDispatchRequested { get; private set; } = false;
        public QcProfile SelectedProfile { get; set; } = QcProfile.FullDiagnostic;

        public ExpressQcRunnerWindow(MainViewModel viewModel, QcProfile initialProfile = QcProfile.FullDiagnostic)
        {
            InitializeComponent();
            _viewModel = viewModel;
            SelectedProfile = initialProfile;

            Loaded += ExpressQcRunnerWindow_Loaded;
            Closing += ExpressQcRunnerWindow_Closing;
        }

        private async void ExpressQcRunnerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ExpressQcEngineService.Instance.SubsystemUpdated += OnSubsystemUpdated;
            ExpressQcEngineService.Instance.OverallProgressChanged += OnOverallProgressChanged;
            ExpressQcEngineService.Instance.Completed += OnCompleted;

            UpdateTechnicianBadge();
            SyncProfileSelectionUi();

            // Start autonomous diagnostic suite with selected profile
            await StartDiagnosticRunAsync();
        }

        private void UpdateTechnicianBadge()
        {
            try
            {
                TxtActiveQcTech.Text = TechnicianProfileService.Instance.CurrentProfile.DisplayBadge;
            }
            catch
            {
                TxtActiveQcTech.Text = "TECH-01";
            }
        }

        private void SyncProfileSelectionUi()
        {
            if (CmbQcProfile == null) return;
            foreach (System.Windows.Controls.ComboBoxItem item in CmbQcProfile.Items)
            {
                if (item.Tag?.ToString() == SelectedProfile.ToString())
                {
                    CmbQcProfile.SelectedItem = item;
                    break;
                }
            }
        }

        private async Task StartDiagnosticRunAsync()
        {
            CmbQcProfile.IsEnabled = false;
            BtnSwitchTech.IsEnabled = false;
            BtnRerunQc.Visibility = Visibility.Collapsed;
            TxtRunnerState.Text = $"● ACTIVE AUDIT [{SelectedProfile}]";

            Report = await ExpressQcEngineService.Instance.RunFullExpressQcAsync(
                _viewModel,
                SelectedProfile,
                TechnicianProfileService.Instance.CurrentProfile.Name);
        }

        private async void BtnRerunQc_Click(object sender, RoutedEventArgs e)
        {
            if (ExpressQcEngineService.Instance.IsRunning) return;
            await StartDiagnosticRunAsync();
        }

        private void CmbQcProfile_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (CmbQcProfile?.SelectedItem is System.Windows.Controls.ComboBoxItem item && item.Tag != null)
            {
                if (Enum.TryParse<QcProfile>(item.Tag.ToString(), out var prof))
                {
                    SelectedProfile = prof;
                }
            }
        }

        private void BtnSwitchTech_Click(object sender, RoutedEventArgs e)
        {
            var modal = new TechnicianModalWindow { Owner = this };
            modal.ShowDialog();
            UpdateTechnicianBadge();
        }


        private void ExpressQcRunnerWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Unsubscribe to avoid memory leaks
            ExpressQcEngineService.Instance.SubsystemUpdated -= OnSubsystemUpdated;
            ExpressQcEngineService.Instance.OverallProgressChanged -= OnOverallProgressChanged;
            ExpressQcEngineService.Instance.Completed -= OnCompleted;

            if (ExpressQcEngineService.Instance.IsRunning)
            {
                ExpressQcEngineService.Instance.Cancel();
            }
        }

        private void OnOverallProgressChanged(int percent, string message)
        {
            Dispatcher.Invoke(() =>
            {
                ProgRunner.Value = percent;
                TxtProgressPct.Text = $"{percent}%";
                TxtStatusDetail.Text = message;
            });
        }

        private void OnSubsystemUpdated(ExpressSubsystemResult res)
        {
            Dispatcher.Invoke(() =>
            {
                var accentBrush = GetBrush(res.AccentHex);

                switch (res.Key)
                {
                    case "Camera":
                        BadgeCam.Text = res.StatusBadge;
                        BadgeCam.Foreground = accentBrush;
                        CardCam.BorderBrush = accentBrush;
                        HeadlineCam.Text = res.Headline;
                        DetailCam.Text = res.Detail;
                        MetricCam.Text = res.MetricValue;
                        if (res.SnapshotImage != null)
                        {
                            ImgCamThumb.Source = res.SnapshotImage;
                        }
                        break;

                    case "Mic":
                        BadgeMic.Text = res.StatusBadge;
                        BadgeMic.Foreground = accentBrush;
                        CardMic.BorderBrush = accentBrush;
                        HeadlineMic.Text = res.Headline;
                        DetailMic.Text = res.Detail;
                        MetricMic.Text = res.MetricValue;
                        ProgMicVu.Value = Math.Clamp((int)(res.SignalLevel * 100), 0, 100);
                        break;

                    case "Speaker":
                        BadgeSpk.Text = res.StatusBadge;
                        BadgeSpk.Foreground = accentBrush;
                        CardSpk.BorderBrush = accentBrush;
                        HeadlineSpk.Text = res.Headline;
                        DetailSpk.Text = res.Detail;
                        MetricSpk.Text = res.MetricValue;
                        break;

                    case "Storage":
                        BadgeStore.Text = res.StatusBadge;
                        BadgeStore.Foreground = accentBrush;
                        CardStore.BorderBrush = accentBrush;
                        HeadlineStore.Text = res.Headline;
                        DetailStore.Text = res.Detail;
                        MetricStore.Text = res.MetricValue;
                        break;

                    case "CpuRam":
                        BadgeCpu.Text = res.StatusBadge;
                        BadgeCpu.Foreground = accentBrush;
                        CardCpu.BorderBrush = accentBrush;
                        HeadlineCpu.Text = res.Headline;
                        DetailCpu.Text = res.Detail;
                        MetricCpu.Text = res.MetricValue;
                        break;

                    case "Battery":
                        BadgeBat.Text = res.StatusBadge;
                        BadgeBat.Foreground = accentBrush;
                        CardBat.BorderBrush = accentBrush;
                        HeadlineBat.Text = res.Headline;
                        DetailBat.Text = res.Detail;
                        MetricBat.Text = res.MetricValue;
                        break;

                    case "Display":
                        BadgeDisp.Text = res.StatusBadge;
                        BadgeDisp.Foreground = accentBrush;
                        CardDisp.BorderBrush = accentBrush;
                        HeadlineDisp.Text = res.Headline;
                        DetailDisp.Text = res.Detail;
                        MetricDisp.Text = res.MetricValue;
                        break;

                    case "Network":
                        BadgeNet.Text = res.StatusBadge;
                        BadgeNet.Foreground = accentBrush;
                        CardNet.BorderBrush = accentBrush;
                        HeadlineNet.Text = res.Headline;
                        DetailNet.Text = res.Detail;
                        MetricNet.Text = res.MetricValue;
                        break;
                }
            });
        }

        private void OnCompleted(ExpressQcFullReport report)
        {
            Dispatcher.Invoke(() =>
            {
                Report = report;

                TxtRunnerState.Text = "● AUDIT COMPLETE";
                TxtRunnerState.Foreground = GetBrush("#58A6FF");
                ProgRunner.Value = 100;
                TxtProgressPct.Text = "100%";

                TxtCalculatedGrade.Text = report.CalculatedGrade;
                BorderGradeBadge.Background = report.CalculatedGrade switch
                {
                    "GRADE A+" => GetBrush("#BC8CFF"),
                    "GRADE A" => GetBrush("#3FB950"),
                    "GRADE B" => GetBrush("#D29922"),
                    _ => GetBrush("#F85149")
                };

                TxtAuditSummary.Text = report.AllPassed
                    ? "All 8 Hardware Subsystems Certified Nominal ✓"
                    : $"{report.PassedCount}/{report.TotalCount} Hardware Subsystems Nominal";

                TxtAuditNotes.Text = report.DefectNotes.Count > 0
                    ? string.Join(" | ", report.DefectNotes)
                    : "Automated Power-On Self-Test (POST) passed with 0 hardware faults.";

                BtnAcceptAll.IsEnabled = true;
                BtnCloudDispatch.IsEnabled = true;
                CmbQcProfile.IsEnabled = true;
                BtnSwitchTech.IsEnabled = true;
                BtnRerunQc.Visibility = Visibility.Visible;

                try

                {
                    SystemSounds.Asterisk.Play();
                }
                catch { }
            });
        }

        private Brush GetBrush(string hex)
        {
            if (_brushCache.TryGetValue(hex, out var brush))
                return brush;

            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                brush = new SolidColorBrush(color);
                brush.Freeze();
                _brushCache[hex] = brush;
                return brush;
            }
            catch
            {
                return Brushes.White;
            }
        }

        private void BtnAcceptAll_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void BtnCloudDispatch_Click(object sender, RoutedEventArgs e)
        {
            CloudDispatchRequested = true;
            DialogResult = true;
            Close();
        }

        private void BtnAbort_Click(object sender, RoutedEventArgs e)
        {
            ExpressQcEngineService.Instance.Cancel();
            DialogResult = false;
            Close();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                BtnAbort_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && BtnAcceptAll.IsEnabled)
            {
                BtnAcceptAll_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.S && BtnCloudDispatch.IsEnabled)
            {
                BtnCloudDispatch_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }
    }
}
