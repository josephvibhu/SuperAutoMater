using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AForge.Video;
using AForge.Video.DirectShow;
using NAudio.Wave;
using VeloxBench.Controls;
using VeloxBench.Services;
using VeloxBench.ViewModels;
using VeloxBench.Views;

namespace VeloxBench;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    private string _activeWorkspace = "telemetry";

    // Camera & Mic
    private FilterInfoCollection? _workspaceVideoDevices;
    private VideoCaptureDevice? _workspaceVideoSource;
    private WaveInEvent? _workspaceWaveIn;
    private bool _micActivityDetected = false;

    // Fingerprint
    private CancellationTokenSource? _fpCts;
    private DispatcherTimer? _fpAnimTimer;
    private float _fpPulseScale = 1.0f;
    private bool _fpPulseGrowing = true;
    private bool _fpSensorTouched = false;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = MainViewModel.Instance;
        Closed += MainWindow_Closed;
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        StopWorkspaceCamera();
        StopWorkspaceMic();
        StopFingerprintListener();
    }

    #region Workspace Management

    public void SwitchWorkspace(string view)
    {
        _activeWorkspace = view.ToLowerInvariant();

        // Hide all views first
        ViewTelemetryArena.Visibility = Visibility.Collapsed;
        ViewKeyboardWorkspace.Visibility = Visibility.Collapsed;
        ViewCamMicWorkspace.Visibility = Visibility.Collapsed;
        ViewFingerprintWorkspace.Visibility = Visibility.Collapsed;
        ViewProvisionWorkspace.Visibility = Visibility.Collapsed;
        BtnExitWorkspace.Visibility = Visibility.Visible;

        // Stop non-applicable background streams
        if (_activeWorkspace != "cammic")
        {
            StopWorkspaceCamera();
            StopWorkspaceMic();
        }
        if (_activeWorkspace != "fingerprint")
        {
            StopFingerprintListener();
        }

        switch (_activeWorkspace)
        {
            case "keyboard":
                ViewKeyboardWorkspace.Visibility = Visibility.Visible;
                TxtWorkspaceTitle.Text = "KEYBOARD & TRACKPAD WORKSPACE";
                TxtWorkspaceSubtitle.Text = " — Interactive key actuation matrix, dynamic numpad & tracking puck";
                KbControl.Focus();
                break;

            case "cammic":
                ViewCamMicWorkspace.Visibility = Visibility.Visible;
                TxtWorkspaceTitle.Text = "WEBCAM & MICROPHONE ARRAY";
                TxtWorkspaceSubtitle.Text = " — Native real-time optical viewfinder & NAudio acoustic VU telemetry";
                InitAndStartWorkspaceCamMic();
                break;

            case "fingerprint":
                ViewFingerprintWorkspace.Visibility = Visibility.Visible;
                TxtWorkspaceTitle.Text = "BIOMETRIC FINGERPRINT SENSOR";
                TxtWorkspaceSubtitle.Text = " — Windows Biometric Framework (WBF) & PnP hardware touch verifier";
                StartFingerprintWorkflow();
                break;

            case "provision":
                ViewProvisionWorkspace.Visibility = Visibility.Visible;
                TxtWorkspaceTitle.Text = "AUTO-PROVISIONING & FACTORY RESET";
                TxtWorkspaceSubtitle.Text = " — DISM driver harvesting, telemetry sanitization & Windows Sysprep OOBE";
                _ = ProvisionControl.RefreshPreFlightAsync();
                break;

            case "telemetry":
            default:
                _activeWorkspace = "telemetry";
                ViewTelemetryArena.Visibility = Visibility.Visible;
                BtnExitWorkspace.Visibility = Visibility.Collapsed;
                TxtWorkspaceTitle.Text = "LIVE TELEMETRY ARENA";
                TxtWorkspaceSubtitle.Text = " — Real-time sensor feeds, benchmarks & optical captures";
                break;
        }
    }

    private void BtnExitWorkspace_Click(object sender, RoutedEventArgs e) => SwitchWorkspace("telemetry");
    private void BtnExitKeyboard_Click(object sender, RoutedEventArgs e) => SwitchWorkspace("telemetry");
    private void BtnExitCamMic_Click(object sender, RoutedEventArgs e) => SwitchWorkspace("telemetry");
    private void BtnExitFingerprint_Click(object sender, RoutedEventArgs e) => SwitchWorkspace("telemetry");
    private void BtnProvision_Click(object sender, RoutedEventArgs e) => SwitchWorkspace("provision");

    private void BtnPassKeyboard_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.MarkSubsystemPassed("Keyboard", "✓ KEYBOARD & TRACKPAD CERTIFIED NOMINAL", "All actuated keys & trackpad buttons verified nominal.");
        }
        VeloxMessageBox.Show("Keyboard matrix and trackpad certified nominal!", "Subsystem Passed", MessageBoxButton.OK, MessageBoxImage.Information);
        SwitchWorkspace("telemetry");
    }

    private void BtnCertifyCamMic_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.MarkSubsystemPassed("Camera", "✓ OPTICAL SENSOR VERIFIED NOMINAL", "Live video feed operational @ 30 FPS.");
            vm.MarkSubsystemPassed("Mic", "✓ MICROPHONE ARRAY NOMINAL", "Acoustic transduction validated.");
        }
        VeloxMessageBox.Show("Webcam optical stream and microphone array certified nominal!", "Subsystem Passed", MessageBoxButton.OK, MessageBoxImage.Information);
        SwitchWorkspace("telemetry");
    }

    private void BtnPassFingerprint_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.MarkSubsystemPassed("Fingerprint", "✓ BIOMETRIC SENSOR CERTIFIED", "Hardware sensor response confirmed nominal.");
        }
        VeloxMessageBox.Show("Biometric fingerprint sensor certified nominal!", "Subsystem Passed", MessageBoxButton.OK, MessageBoxImage.Information);
        SwitchWorkspace("telemetry");
    }

    #endregion

    #region Webcam & Mic Handling

    private void InitAndStartWorkspaceCamMic()
    {
        try
        {
            _workspaceVideoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            CmbWorkspaceCameras.Items.Clear();

            if (_workspaceVideoDevices.Count == 0)
            {
                StackCamNoDevice.Visibility = Visibility.Visible;
                TxtCamMessage.Text = "No video capture device detected.";
                TxtCamStatus.Text = "OFFLINE";
                DotCamStatus.Fill = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#D29922"));
            }
            else
            {
                StackCamNoDevice.Visibility = Visibility.Collapsed;
                foreach (FilterInfo dev in _workspaceVideoDevices)
                {
                    CmbWorkspaceCameras.Items.Add(dev.Name);
                }
                CmbWorkspaceCameras.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            StackCamNoDevice.Visibility = Visibility.Visible;
            TxtCamMessage.Text = "Camera Probe Error: " + ex.Message;
        }

        StartWorkspaceMic();
    }

    private void CmbWorkspaceCameras_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbWorkspaceCameras.SelectedIndex >= 0 && _workspaceVideoDevices != null && CmbWorkspaceCameras.SelectedIndex < _workspaceVideoDevices.Count)
        {
            StartWorkspaceCamera(_workspaceVideoDevices[CmbWorkspaceCameras.SelectedIndex].MonikerString);
        }
    }

    private void StartWorkspaceCamera(string monikerString)
    {
        StopWorkspaceCamera();
        try
        {
            _workspaceVideoSource = new VideoCaptureDevice(monikerString);
            _workspaceVideoSource.NewFrame += WorkspaceVideoSource_NewFrame;
            _workspaceVideoSource.Start();

            StackCamNoDevice.Visibility = Visibility.Collapsed;
            TxtCamStatus.Text = "LIVE STREAM ACTIVE";
            DotCamStatus.Fill = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3FB950"));
        }
        catch (Exception ex)
        {
            StackCamNoDevice.Visibility = Visibility.Visible;
            TxtCamMessage.Text = "Camera Error: " + ex.Message;
        }
    }

    private void WorkspaceVideoSource_NewFrame(object sender, NewFrameEventArgs eventArgs)
    {
        try
        {
            using var bitmap = (Bitmap)eventArgs.Frame.Clone();
            IntPtr hBitmap = bitmap.GetHbitmap();
            try
            {
                var bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                bitmapSource.Freeze();

                Dispatcher.InvokeAsync(() =>
                {
                    ImgWorkspaceCam.Source = bitmapSource;
                }, DispatcherPriority.Render);
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }
        catch { }
    }

    private void StopWorkspaceCamera()
    {
        if (_workspaceVideoSource != null)
        {
            try
            {
                _workspaceVideoSource.SignalToStop();
                _workspaceVideoSource.NewFrame -= WorkspaceVideoSource_NewFrame;
                _workspaceVideoSource = null;
            }
            catch { }
        }
        Dispatcher.InvokeAsync(() =>
        {
            ImgWorkspaceCam.Source = null;
        });
    }

    private void StartWorkspaceMic()
    {
        if (_workspaceWaveIn != null) return;

        try
        {
            if (WaveInEvent.DeviceCount == 0)
            {
                TxtMicDeviceName.Text = "No audio input devices found";
                TxtMicStatus.Text = "○ NO MIC";
                TxtMicStatus.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#D29922"));
                return;
            }

            var caps = WaveInEvent.GetCapabilities(0);
            TxtMicDeviceName.Text = caps.ProductName;

            _workspaceWaveIn = new WaveInEvent
            {
                DeviceNumber = 0,
                WaveFormat = new WaveFormat(44100, 16, 1),
                BufferMilliseconds = 50
            };
            _workspaceWaveIn.DataAvailable += WorkspaceWaveIn_DataAvailable;
            _workspaceWaveIn.StartRecording();

            TxtMicStatus.Text = "● LISTENING";
            TxtMicStatus.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3FB950"));
        }
        catch (Exception ex)
        {
            TxtMicDeviceName.Text = "Mic Error: " + ex.Message;
        }
    }

    private void WorkspaceWaveIn_DataAvailable(object? sender, WaveInEventArgs e)
    {
        float maxSample = 0;
        for (int i = 0; i < e.BytesRecorded; i += 2)
        {
            short sample = (short)((e.Buffer[i + 1] << 8) | e.Buffer[i]);
            float sample32 = Math.Abs(sample / 32768f);
            if (sample32 > maxSample) maxSample = sample32;
        }

        double db = 20.0 * Math.Log10(Math.Max(maxSample, 0.0001));
        int vuPercent = (int)Math.Max(0, Math.Min(100, (db + 60.0) * (100.0 / 60.0)));

        Dispatcher.InvokeAsync(() =>
        {
            ProgBarMicLevel.Value = vuPercent;
            if (maxSample > 0.005)
            {
                TxtMicDbLevel.Text = $"{db:0.0} dB";
            }
            else
            {
                TxtMicDbLevel.Text = "-∞ dB (Idle)";
            }

            if (vuPercent > 20)
            {
                TxtMicStatus.Text = "● AUDIO DETECTED";
                TxtMicStatus.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3FB950"));

                if (!_micActivityDetected)
                {
                    _micActivityDetected = true;
                    BorderMicCertified.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x40, 0x10, 0xB9, 0x81));
                    TxtMicInstruction.Text = "✓ Audio signal captured! Transducer operational.";
                }
            }
        }, DispatcherPriority.Background);
    }

    private void StopWorkspaceMic()
    {
        if (_workspaceWaveIn != null)
        {
            try
            {
                _workspaceWaveIn.DataAvailable -= WorkspaceWaveIn_DataAvailable;
                _workspaceWaveIn.StopRecording();
                _workspaceWaveIn.Dispose();
                _workspaceWaveIn = null;
            }
            catch { }
        }
    }

    #endregion

    #region Biometric Fingerprint Handling

    private void StartFingerprintWorkflow()
    {
        _fpSensorTouched = false;
        TxtFpStatus.Text = "PROBING SENSOR HARDWARE...";
        TxtFpPassStatus.Text = "Scanning Windows Biometric Framework (WBF)...";

        if (_fpAnimTimer == null)
        {
            _fpAnimTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            _fpAnimTimer.Tick += (s, e) =>
            {
                if (_fpPulseGrowing)
                {
                    _fpPulseScale += 0.02f;
                    if (_fpPulseScale >= 1.15f) _fpPulseGrowing = false;
                }
                else
                {
                    _fpPulseScale -= 0.02f;
                    if (_fpPulseScale <= 0.95f) _fpPulseGrowing = true;
                }

                if (!_fpSensorTouched)
                {
                    FpRipple1.Width = Math.Max(20, 110 * _fpPulseScale);
                    FpRipple1.Height = Math.Max(20, 110 * _fpPulseScale);
                    FpRipple2.Width = Math.Max(30, 136 * _fpPulseScale);
                    FpRipple2.Height = Math.Max(30, 136 * _fpPulseScale);
                }
            };
        }
        _fpAnimTimer.Start();

        ProbeAndListenFingerprint();
    }

    private async void ProbeAndListenFingerprint()
    {
        StopFingerprintListener();
        _fpCts = new CancellationTokenSource();

        var info = await FingerprintService.Instance.ProbeSensorAsync();

        TxtFpDeviceName.Text = info.Name;
        TxtFpMfg.Text = info.Manufacturer;
        TxtFpUnitId.Text = info.HasSensor ? $"Unit #{info.UnitId}" : "N/A";
        TxtFpDeviceId.Text = info.DeviceId;
        TxtFpSourceTag.Text = info.Source.Contains("WBF") ? "WBF" : (info.HasSensor ? "PNP" : "NONE");

        if (info.HasSensor)
        {
            TxtFpStatus.Text = "👆 WAITING FOR FINGERPRINT TOUCH...";
            TxtFpHint.Text = "Touch or swipe your finger across the physical sensor now";
            TxtFpPassStatus.Text = $"Sensor active ({info.Name}) · Listening for hardware touch";
            BorderFpStatus.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x30, 0x38, 0x8B, 0xFD));
            BorderFpStatus.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0x38, 0x8B, 0xFD));
            TxtFpStatus.Foreground = System.Windows.Media.Brushes.White;

            var ct = _fpCts.Token;
            bool touched = await FingerprintService.Instance.ListenForTouchAsync(ct, unitId =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    OnFingerprintTouchDetected(unitId);
                });
            });
        }
        else
        {
            TxtFpStatus.Text = "⚠ NO BIOMETRIC SENSOR DETECTED";
            TxtFpHint.Text = "No physical fingerprint hardware detected. Ready for manual pass if external.";
            TxtFpPassStatus.Text = "No hardware sensor detected · Ready for sign-off";
            BorderFpStatus.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x30, 0xD2, 0x99, 0x22));
            BorderFpStatus.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0xD2, 0x99, 0x22));
            TxtFpStatus.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#D29922"));
        }
    }

    private void OnFingerprintTouchDetected(uint unitId)
    {
        _fpSensorTouched = true;
        BorderFpStatus.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x30, 0x2E, 0xA0, 0x43));
        BorderFpStatus.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0x2E, 0xA0, 0x43));
        TxtFpStatus.Text = $"✓ SENSOR TOUCH CONFIRMED (UNIT #{unitId})!";
        TxtFpStatus.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3FB950"));
        TxtFpHint.Text = "Hardware biometric response verified 100% nominal";
        TxtFpPassStatus.Text = "Biometric Sensor Tested & Passed Nominal ✓";

        BorderFpTarget.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3FB950"));
        BorderFpTarget.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x40, 0x10, 0xB9, 0x81));

        if (DataContext is MainViewModel vm)
        {
            vm.MarkSubsystemPassed("Fingerprint", "✓ BIOMETRIC TOUCH VERIFIED", $"Unit #{unitId} touch registered nominal.");
        }
    }

    private void StopFingerprintListener()
    {
        _fpAnimTimer?.Stop();
        if (_fpCts != null)
        {
            try
            {
                _fpCts.Cancel();
                _fpCts.Dispose();
            }
            catch { }
            _fpCts = null;
        }
        FingerprintService.Instance.CleanupSession();
    }

    private void BtnFpRescan_Click(object sender, RoutedEventArgs e)
    {
        ProbeAndListenFingerprint();
    }

    #endregion

    #region Hotkeys & Navigation

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var vm = DataContext as MainViewModel;
        if (vm == null) return;

        // Escape -> Return to default telemetry arena if inside a workspace
        if (e.Key == Key.Escape && _activeWorkspace != "telemetry")
        {
            e.Handled = true;
            SwitchWorkspace("telemetry");
            return;
        }

        // F5 -> Run Full 10-Test Express QC
        if (e.Key == Key.F5)
        {
            e.Handled = true;
            if (vm.RunQcCommand.CanExecute(null))
                vm.RunQcCommand.Execute(null);
            return;
        }

        // F8 -> Advanced 14-Stage Display Diagnostic Lab
        if (e.Key == Key.F8)
        {
            e.Handled = true;
            LaunchDisplayDiagnosticLab();
            return;
        }

        // F10 -> Keyboard & Trackpad Workspace
        if (e.Key == Key.F10)
        {
            e.Handled = true;
            SwitchWorkspace("keyboard");
            return;
        }

        // F11 -> Fingerprint Workspace
        if (e.Key == Key.F11)
        {
            e.Handled = true;
            SwitchWorkspace("fingerprint");
            return;
        }

        // F12 -> Auto-Provisioning & Factory Reset Workspace
        if (e.Key == Key.F12)
        {
            e.Handled = true;
            SwitchWorkspace("provision");
            return;
        }

        // F1 -> Webcam & Mic Workspace
        if (e.Key == Key.F1)
        {
            e.Handled = true;
            SwitchWorkspace("cammic");
            return;
        }

        // On-Demand Single Subsystem Diagnostic Hotkeys
        string? singleKey = e.Key switch
        {
            Key.F2 => "Mic",
            Key.F3 => "Speaker",
            Key.F4 => "Storage",
            Key.F6 => "CpuRam",
            Key.F7 => "Battery",
            Key.F9 => "Network",
            _ => null
        };

        if (singleKey != null)
        {
            e.Handled = true;
            if (vm.RunSingleSubsystemCommand.CanExecute(singleKey))
                vm.RunSingleSubsystemCommand.Execute(singleKey);
            return;
        }

        // Ctrl+P -> Print Certificate
        if (e.Key == Key.P && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            e.Handled = true;
            if (vm.GeneratePdfCommand.CanExecute(null))
                vm.GeneratePdfCommand.Execute(null);
            return;
        }

        // Enter -> Pass QC & Push Cloud (when not typing in a multi-line text input)
        if (e.Key == Key.Enter && !(e.OriginalSource is TextBox tb && tb.AcceptsReturn))
        {
            e.Handled = true;
            if (vm.PassQcAndPushCloudCommand.CanExecute(null))
                vm.PassQcAndPushCloudCommand.Execute(null);
            return;
        }
    }

    private void LaunchDisplayDiagnosticLab()
    {
        var dispWin = new DisplayTestWindow { Owner = this };
        dispWin.ShowDialog();
        if (dispWin.TestPassed && DataContext is MainViewModel vm)
        {
            vm.MarkSubsystemPassed("Display", "✓ 14-STAGE DISPLAY LAB PASSED", "Full RGB, grayscale gamma, typography & motion nominal.");
        }
    }

    private void SubsystemCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is Services.ExpressSubsystemResult sub)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.SelectedSubsystem = sub;
            }

            // Route to appropriate workspace or modal
            switch (sub.Key.ToLowerInvariant())
            {
                case "keyboard":
                    SwitchWorkspace("keyboard");
                    break;
                case "camera":
                case "mic":
                    SwitchWorkspace("cammic");
                    break;
                case "fingerprint":
                    SwitchWorkspace("fingerprint");
                    break;
                case "display":
                    LaunchDisplayDiagnosticLab();
                    break;
                default:
                    SwitchWorkspace("telemetry");
                    break;
            }
        }
    }

    #endregion
}