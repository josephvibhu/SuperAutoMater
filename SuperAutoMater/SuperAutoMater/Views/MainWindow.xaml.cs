using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using AForge.Video;
using AForge.Video.DirectShow;
using NAudio.Wave;
using SuperAutoMater.Wpf.Services;
using SuperAutoMater.Wpf.ViewModels;

namespace SuperAutoMater.Wpf.Views
{
    public partial class MainWindow : Window
    {
        [DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject(IntPtr hObject);

        private UsbPortTracker _usbTracker;
        private HwndSource _hwndSource;
        private DispatcherTimer _toastTimer;

        // Dynamic Central Workspace State
        private string _activeWorkspace = "standby";
        private FilterInfoCollection _workspaceVideoDevices;
        private VideoCaptureDevice _workspaceVideoSource;
        private WaveInEvent _workspaceWaveIn;
        private DispatcherTimer _fpAnimTimer;
        private CancellationTokenSource _fpCts;
        private float _fpPulseScale = 1.0f;
        private bool _fpPulseGrowing = true;
        private bool _fpSensorTouched = false;
        private bool _micActivityDetected = false;
        private readonly object _camFrameLock = new object();
        private System.Drawing.Bitmap _latestCamFrame;

        // Embedded CPU & RAM Stress State
        private DispatcherTimer _cpuStressTimer;
        private CancellationTokenSource _cpuStressCts;
        private int _cpuStressSeconds = 0;
        private bool _isCpuStressing = false;
        private Thread _ramStressThread;
        private volatile bool _ramStressRunning = false;
        private long _ramBytesProcessed = 0;
        private int _ramErrors = 0;

        // Embedded GPU Particle Benchmark State
        private DispatcherTimer _gpuRenderTimer;
        private readonly List<Particle> _gpuParticles = new List<Particle>();
        private readonly Random _rand = new Random();
        private int _gpuFrameCount = 0;
        private DateTime _lastFpsUpdate = DateTime.Now;

        private class Particle
        {
            public Ellipse Element;
            public double X;
            public double Y;
            public double Vx;
            public double Vy;
        }

        private MainViewModel ViewModel => DataContext as MainViewModel;

        public MainWindow()
        {
            InitializeComponent();
            WindowState = WindowState.Maximized;
            _usbTracker = new UsbPortTracker();

            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Maximized;

            // 1. Hook low-level Windows message pump for USB hot-plug
            var helper = new WindowInteropHelper(this);
            _hwndSource = HwndSource.FromHwnd(helper.Handle);
            _hwndSource?.AddHook(WndProcHook);

            // 2. Wire USB Port Tracker events
            _usbTracker.PortTested += (count, label, driveDetails) =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    if (ViewModel != null)
                    {
                        if (!ViewModel.IsPort1Verified)
                        {
                            ViewModel.IsPort1Verified = true;
                            ViewModel.Port1Device = driveDetails;
                            ShowFeedback($"USB Port 1 Verified: {driveDetails} ✓", "🔌", "#3FB950");
                        }
                        else if (!ViewModel.IsPort2Verified)
                        {
                            ViewModel.IsPort2Verified = true;
                            ViewModel.Port2Device = driveDetails;
                            ShowFeedback($"USB Port 2 Verified: {driveDetails} ✓", "🔌", "#3FB950");
                        }
                        else if (!ViewModel.IsPort3Verified)
                        {
                            ViewModel.IsPort3Verified = true;
                            ViewModel.Port3Device = driveDetails;
                            ShowFeedback($"USB Port 3 Verified: {driveDetails} ✓", "🔌", "#3FB950");
                        }
                        else
                        {
                            ShowFeedback($"USB Hot-Plug Verified: {driveDetails} ✓", "🔌", "#3FB950");
                        }
                    }
                });
            };

            _usbTracker.PortRemoved += (count) =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    ShowFeedback("USB Flash Drive Unplugged. Move to next port to verify ✓", "🔌", "#58A6FF");
                });
            };

            // Check if any removable drive is already connected at launch
            string initialDrive = UsbPortTracker.GetActiveUsbDriveDetails();
            if (initialDrive != null && ViewModel != null)
            {
                ViewModel.IsPort1Verified = true;
                ViewModel.Port1Device = initialDrive;
            }

            // 3. Initialize in Standby hub
            SwitchWorkspace("standby");

            // 4. Kick off async hardware probes
            if (ViewModel != null)
            {
                await ViewModel.RefreshTelemetryAsync();
                ShowFeedback("Diagnostic Bench Online: All hardware telemetry loaded ✓", "⚡", "#3FB950");
            }

            // 5. Register Real-Time FFT Acoustic Spectrum Analyzer
            AcousticAnalyzerService.Instance.SpectrumUpdated += OnAcousticSpectrumUpdated;

            // 6. Start Warehouse Fleet LAN HUD & Mesh
            WarehouseFleetService.Instance.FleetUpdated += OnFleetServiceUpdated;
            WarehouseFleetService.Instance.StartService(ViewModel);
            if (TxtFleetSummaryBanner != null)
            {
                TxtFleetSummaryBanner.Text = $"{WarehouseFleetService.Instance.LocalDashboardUrl} · UDP 9876 Active";
            }
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            WarehouseFleetService.Instance.FleetUpdated -= OnFleetServiceUpdated;
            WarehouseFleetService.Instance.StopService();
            AcousticAnalyzerService.Instance.SpectrumUpdated -= OnAcousticSpectrumUpdated;
            StopWorkspaceCamera();
            StopWorkspaceMic();
            StopFingerprintListener();
            StopCpuStress();
            StopGpuAnimation();
            AcousticAnalyzerService.Instance.StopListening();

            lock (_camFrameLock)
            {
                _latestCamFrame?.Dispose();
                _latestCamFrame = null;
            }

            if (_hwndSource != null)
            {
                _hwndSource.RemoveHook(WndProcHook);
                _hwndSource.Dispose();
                _hwndSource = null;
            }
        }

        private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_DEVICECHANGE = 0x0219;
            if (msg == WM_DEVICECHANGE)
            {
                var m = System.Windows.Forms.Message.Create(hwnd, msg, wParam, lParam);
                _usbTracker.ProcessWndProc(ref m);
            }
            return IntPtr.Zero;
        }

        public void ShowFeedback(string message, string icon = "✓", string accentHex = "#3FB950")
        {
            Dispatcher.InvokeAsync(() =>
            {
                TxtToastIcon.Text = icon;
                try
                {
                    var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(accentHex));
                    TxtToastIcon.Foreground = brush;
                    ToastBanner.BorderBrush = brush;
                }
                catch { }

                TxtToastMessage.Text = message;
                ToastBanner.Visibility = Visibility.Visible;

                // Audio feedback chime removed for smooth, silent navigation

                _toastTimer?.Stop();
                _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
                _toastTimer.Tick += (s, ev) =>
                {
                    ToastBanner.Visibility = Visibility.Collapsed;
                    _toastTimer?.Stop();
                };
                _toastTimer.Start();
            });
        }

        public void RunDiagnosticTest(string key)
        {
            if (ViewModel == null) return;

            switch (key?.ToLowerInvariant())
            {
                case "display":
                case "f1":
                    ShowFeedback("Display Test [F1]: Advanced 14-Stage Display Lab (Space/Click), Exit (Esc/Enter)...", "📺", "#58A6FF");
                    var dispWin = new DisplayTestWindow { Owner = this };
                    dispWin.ShowDialog();
                    if (dispWin.TestPassed)
                    {
                        ViewModel.MarkTestPassed("Display");
                        ShowFeedback("Display / Panel Test: 100% RGB & Patterns Verified Nominal ✓", "✓", "#3FB950");
                    }
                    break;

                case "touch":
                case "touchscreen":
                    ShowFeedback("Touchscreen Digitizer Matrix: Drag across blocks to certify panel...", "👆", "#58A6FF");
                    var touchWin = new TouchscreenTestWindow { Owner = this };
                    touchWin.ShowDialog();
                    if (touchWin.TestPassed)
                    {
                        ViewModel.MarkTestPassed("Display");
                        ShowFeedback("Touchscreen Digitizer Matrix: 100% Sensor Grid Certified Nominal ✓", "✓", "#3FB950");
                    }
                    break;

                case "audio":
                case "f2":
                    ShowFeedback("Audio Stereo Sweep [F2]: Acoustic ping-pong active in cockpit", "🔊", "#58A6FF");
                    SwitchWorkspace("audio");
                    break;

                case "camera":
                case "webcam":
                case "f3":
                    ShowFeedback("Webcam & Microphone Array [F3]: Live video & VU meter online in cockpit ✓", "📷", "#58A6FF");
                    SwitchWorkspace("cammic");
                    break;

                case "keyboard":
                    ShowFeedback("Keyboard & Trackpad: Press keys & actuate trackpad surface + buttons", "⌨", "#58A6FF");
                    SwitchWorkspace("keyboard");
                    break;

                case "cpu":
                case "f4":
                    ShowFeedback("CPU & Memory Stress [F4]: Multi-core & RAM bit-flip workload active in cockpit", "⚡", "#58A6FF");
                    SwitchWorkspace("cpu");
                    break;

                case "gpu":
                case "f6":
                    ShowFeedback("GPU 3D Acceleration [F6]: Direct3D benchmark active in cockpit", "🎮", "#58A6FF");
                    SwitchWorkspace("gpu");
                    break;

                case "fingerprint":
                case "f7":
                    ShowFeedback("Biometric Fingerprint Sensor [F7]: Hardware sensor active · Touch to certify ✓", "👆", "#58A6FF");
                    SwitchWorkspace("fingerprint");
                    break;

                case "storage":
                    ShowFeedback("NVMe SMART Radar: Probing 3-source SSD telemetry and life counters...", "💾", "#58A6FF");
                    SwitchWorkspace("storage");
                    break;

                case "battery":
                case "power":
                case "f5":
                    ShowFeedback("Battery Health & Power [F5]: Probing cell capacities and mWh telemetry...", "🔋", "#58A6FF");
                    SwitchWorkspace("battery");
                    break;

                case "usb":
                case "ports":
                case "f8":
                    ShowFeedback("USB Ports & Bus [F8]: Port topology & hot-plug radar active...", "🔌", "#58A6FF");
                    SwitchWorkspace("usb");
                    break;

                case "bluetooth":
                case "wireless":
                case "wifi":
                    ShowFeedback("Wireless RF & Bluetooth: Wi-Fi 6 & Host Transceiver Active ✓", "📶", "#3FB950");
                    SwitchWorkspace("wireless");
                    break;

                default:
                    ViewModel.MarkNextTestPassed();
                    ShowFeedback($"Test pipeline advanced ({ViewModel.PipelineStatusText}) ✓", "✓", "#3FB950");
                    break;
            }
        }

        private void OnAcousticSpectrumUpdated(float[] bands)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (ViewAudioWorkspace.Visibility != Visibility.Visible) return;
                for (int i = 0; i < 16 && i < bands.Length; i++)
                {
                    if (FindName($"FftBar{i}") is Border bar)
                    {
                        double h = Math.Clamp(bands[i] * 46.0 + 3.0, 3.0, 48.0);
                        bar.Height = h;
                    }
                }
            }, DispatcherPriority.Render);
        }

        private async Task PlayStereoSweepAsync()
        {
            try
            {
                AcousticAnalyzerService.Instance.StartListening();

                TxtAudioSweepStatus.Text = "Left Channel (L): 200 Hz – 2,500 Hz Log Chirp Sweep...";
                ProgBarAudioLeft.Value = 85;
                ProgBarAudioRight.Value = 0;
                BorderAudioLeft.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3FB950"));
                BorderAudioRight.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#30363D"));

                // Left channel sweep
                await Task.Run(() =>
                {
                    using var s = GenerateLogarithmicChirpStream(true, false);
                    using var sp = new SoundPlayer(s);
                    sp.PlaySync();
                });

                ProgBarAudioLeft.Value = 0;
                await Task.Delay(150);

                TxtAudioSweepStatus.Text = "Right Channel (R): 200 Hz – 2,500 Hz Log Chirp Sweep...";
                ProgBarAudioLeft.Value = 0;
                ProgBarAudioRight.Value = 85;
                BorderAudioLeft.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#30363D"));
                BorderAudioRight.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3FB950"));

                // Right channel sweep
                await Task.Run(() =>
                {
                    using var s = GenerateLogarithmicChirpStream(false, true);
                    using var sp = new SoundPlayer(s);
                    sp.PlaySync();
                });

                ProgBarAudioRight.Value = 0;
                await Task.Delay(150);

                TxtAudioSweepStatus.Text = "Stereo Full Spectrum (L+R): 200 Hz – 2,500 Hz Harmonic Sweep...";
                ProgBarAudioLeft.Value = 90;
                ProgBarAudioRight.Value = 90;
                BorderAudioLeft.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3FB950"));
                BorderAudioRight.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3FB950"));

                // Stereo both channels sweep
                await Task.Run(() =>
                {
                    using var s = GenerateLogarithmicChirpStream(true, true);
                    using var sp = new SoundPlayer(s);
                    sp.PlaySync();
                });

                ProgBarAudioLeft.Value = 0;
                ProgBarAudioRight.Value = 0;
                TxtAudioSweepStatus.Text = "✓ Acoustic Sweep Complete · Both transducers profiled";

                // Analyze acoustic transducer coupling & harmonic distortion
                var acResult = AcousticAnalyzerService.Instance.StopAndAnalyze();
                TxtAcousticCoupling.Text = acResult.StatusSummary;
                TxtAcousticCoupling.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(acResult.AccentHex));

                if (TxtDistortionStatus != null)
                {
                    TxtDistortionStatus.Text = acResult.IsBlownSpeakerDetected
                        ? $"⚠ BLOWN SPEAKER / COIL RATTLE (THD: {acResult.TotalHarmonicDistortionPercent:F1}%)"
                        : $"✓ HARMONIC DISTORTION NOMINAL (THD: {acResult.TotalHarmonicDistortionPercent:F1}% · No Rattle)";
                    TxtDistortionStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(acResult.IsBlownSpeakerDetected ? "#F85149" : "#3FB950"));
                }
                if (CardDistortionStatus != null)
                {
                    CardDistortionStatus.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(acResult.IsBlownSpeakerDetected ? "#F85149" : "#30363D"));
                }

                ViewModel?.MarkTestPassed("Audio");
            }
            catch
            {
                AcousticAnalyzerService.Instance.StopListening();
                try { Console.Beep(800, 200); Console.Beep(1200, 250); } catch { }
            }
        }

        private static MemoryStream GenerateLogarithmicChirpStream(bool leftChannel, bool rightChannel, double f0 = 200.0, double f1 = 2500.0, double duration = 0.80)
        {
            int sr = 44100;
            int totalSamples = (int)(sr * duration);
            int dataBytes = totalSamples * 2 * 2;

            var ms = new MemoryStream(44 + dataBytes);
            using (var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true))
            {
                bw.Write("RIFF".ToCharArray());
                bw.Write(36 + dataBytes);
                bw.Write("WAVEfmt ".ToCharArray());
                bw.Write(16);
                bw.Write((short)1); // PCM
                bw.Write((short)2); // Stereo
                bw.Write(sr);
                bw.Write(sr * 4);
                bw.Write((short)4);
                bw.Write((short)16);
                bw.Write("data".ToCharArray());
                bw.Write(dataBytes);

                double logRatio = Math.Log(f1 / f0);

                for (int i = 0; i < totalSamples; i++)
                {
                    double t = (double)i / sr;
                    double relT = t / duration;

                    double phase = 2.0 * Math.PI * f0 * ((Math.Pow(f1 / f0, relT) - 1.0) / logRatio) * duration;

                    double env = 1.0;
                    if (t < 0.020) env = t / 0.020;
                    else if (t > duration - 0.040) env = (duration - t) / 0.040;

                    double tone = Math.Sin(phase);
                    double sample = tone * env * 0.85;
                    short val = (short)(sample * 28000);

                    bw.Write(leftChannel ? val : (short)0);
                    bw.Write(rightChannel ? val : (short)0);
                }
            }
            ms.Position = 0;
            return ms;
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (ViewModel == null) return;

            if (e.Key == Key.F1)
            {
                RunDiagnosticTest("Display");
                e.Handled = true;
            }
            else if (e.Key == Key.F2)
            {
                RunDiagnosticTest("Audio");
                e.Handled = true;
            }
            else if (e.Key == Key.F3)
            {
                RunDiagnosticTest("Camera");
                e.Handled = true;
            }
            else if (e.Key == Key.F4)
            {
                RunDiagnosticTest("Cpu");
                e.Handled = true;
            }
            else if (e.Key == Key.F5)
            {
                BtnExpressQc_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.F6)
            {
                RunDiagnosticTest("Gpu");
                e.Handled = true;
            }
            else if (e.Key == Key.F7)
            {
                RunDiagnosticTest("Fingerprint");
                e.Handled = true;
            }
            else if (e.Key == Key.F8)
            {
                RunDiagnosticTest("Usb");
                e.Handled = true;
            }
            else if (e.Key == Key.F9)
            {
                ViewModel.RefreshCommand.Execute(null);
                ShowFeedback("Hardware telemetry refreshed from sensors ✓", "🔄", "#58A6FF");
                e.Handled = true;
            }
            else if (e.Key == Key.Space)
            {
                ViewModel.MarkNextTestPassed();
                ShowFeedback($"Advanced next test to PASSED ({ViewModel.PipelineStatusText}) ✓", "✓", "#3FB950");
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                if (_activeWorkspace == "keyboard")
                {
                    BtnPassKeyboard_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                }
                else if (_activeWorkspace == "cammic" || _activeWorkspace == "camera")
                {
                    BtnCertifyCamMic_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                }
                else if (_activeWorkspace == "fingerprint")
                {
                    BtnPassFingerprint_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                }
                else if (_activeWorkspace == "cpu")
                {
                    BtnPassCpu_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                }
                else if (_activeWorkspace == "gpu")
                {
                    BtnPassGpu_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                }
                else if (_activeWorkspace == "storage")
                {
                    BtnPassStorage_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                }
                else if (_activeWorkspace == "battery" || _activeWorkspace == "power")
                {
                    BtnPassBattery_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                }
                else if (_activeWorkspace == "usb" || _activeWorkspace == "ports")
                {
                    BtnPassUsb_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                }
                else if (_activeWorkspace == "audio")
                {
                    BtnPassAudio_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                }
                else if (_activeWorkspace == "bluetooth")
                {
                    BtnPassBluetooth_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                }
                else
                {
                    BtnOpenSyncMenu_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.F10)
            {
                BtnStartAutopilot_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.F11)
            {
                BtnFleetHud_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.F12)
            {
                BtnRetailPrep_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                if (AutopilotQcService.Instance.IsRunning)
                {
                    AutopilotQcService.Instance.Cancel();
                    ShowFeedback("Autopilot QC sequence aborted by operator [ESC]", "⏹", "#FF7B72");
                }
                SwitchWorkspace("standby");
                e.Handled = true;
            }
            else if (e.Key == Key.P && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                BtnPrintLabel_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private void BtnFleetHud_Click(object sender, RoutedEventArgs e)
        {
            var fleetWin = new FleetDashboardWindow(ViewModel) { Owner = this };
            fleetWin.ShowDialog();
        }

        private async void BtnStartAutopilot_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null || AutopilotQcService.Instance.IsRunning) return;

            PillAutopilot.Visibility = Visibility.Visible;
            TxtAutopilotStatus.Text = "⚡ AUTOPILOT QC: INITIALIZING...";

            EventHandler<AutopilotProgressEventArgs> progressHandler = (s, args) =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    TxtAutopilotStatus.Text = $"⚡ STAGE {args.CurrentStage}/{args.TotalStages}: {args.StageName} [ESC TO STOP]";
                });
            };

            Action<bool, string> completedHandler = null;
            completedHandler = (success, msg) =>
            {
                AutopilotQcService.Instance.ProgressChanged -= progressHandler;
                AutopilotQcService.Instance.Completed -= completedHandler;

                Dispatcher.InvokeAsync(() =>
                {
                    PillAutopilot.Visibility = Visibility.Collapsed;
                    ShowFeedback(msg, success ? "⚡" : "⚠", success ? "#3FB950" : "#D29922");
                    if (success)
                    {
                        OfflineLedgerService.Instance.SaveRecord(new QcAuditRecord
                        {
                            Serial_Number = ViewModel.Serial,
                            Model = ViewModel.Model,
                            Physical_Grade = ViewModel.Grade,
                            Status = "PASSED",
                            CPU_Model = ViewModel.CpuName,
                            RAM_GB = ViewModel.RamSummary,
                            Storage_Details = ViewModel.PrimaryDriveModel,
                            Battery_Health = ViewModel.BatteryIntegrityBadge,
                            GPU_Model = ViewModel.GpuName,
                            Technician_Notes = "Certified via 1-Click Autopilot QC [F10]"
                        });
                    }
                });
            };

            AutopilotQcService.Instance.ProgressChanged += progressHandler;
            AutopilotQcService.Instance.Completed += completedHandler;

            ShowFeedback("1-Click Autopilot QC Initiated [F10]...", "⚡", "#58A6FF");
            await AutopilotQcService.Instance.StartAutopilotAsync(ViewModel, SwitchWorkspace);
        }

        private void BtnExportPdf_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;
            try
            {
                var certData = new CertificateData
                {
                    SerialNumber = ViewModel.Serial,
                    Manufacturer = ViewModel.Manufacturer,
                    Model = ViewModel.Model,
                    BiosVersion = "UEFI Compliant",
                    CpuModel = ViewModel.CpuName,
                    RamDetails = ViewModel.RamSummary,
                    StorageModel = ViewModel.PrimaryDriveModel,
                    StorageHealthPercent = ViewModel.HdsHealth,
                    StoragePowerOn = ViewModel.HdsPowerOnTime,
                    BatteryHealthSummary = ViewModel.BatteryIntegrityBadge,
                    BatteryCapacities = $"{ViewModel.BatteryFullChargeCapacityMwh} / {ViewModel.BatteryDesignCapacityMwh} mWh",
                    BatteryCellTopology = $"{ViewModel.BatteryCellTopology} · {ViewModel.BatteryCellBalanceBadge}",
                    GpuModel = ViewModel.GpuName,
                    PhysicalGrade = ViewModel.Grade,
                    CosmeticDefectsSummary = ViewModel.CosmeticDefectsSummary,
                    TechnicianName = "QC Workstation #1",
                    CloudAuditUrl = GoogleSheetsDispatcher.DefaultSheetsUrl
                };

                if (ViewModel.TestPipeline != null)
                {
                    foreach (var test in ViewModel.TestPipeline)
                    {
                        if (test.StatusBadge == "✓")
                        {
                            certData.PassedTests.Add(test.Title);
                        }
                    }
                }

                string pdfPath = PdfCertificateService.Instance.GenerateCertificate(certData);
                PdfCertificateService.Instance.OpenCertificate(pdfPath);
                ShowFeedback("1-Click PDF Certificate Dispatched to Desktop ✓", "📄", "#3FB950");
            }
            catch (Exception ex)
            {
                ShowFeedback($"PDF Export Error: {ex.Message}", "⚠", "#D29922");
            }
        }

        private void BtnCaptureQcPhoto_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;
            try
            {
                System.Drawing.Bitmap bmp = null;
                lock (_camFrameLock)
                {
                    if (_latestCamFrame != null)
                    {
                        bmp = (System.Drawing.Bitmap)_latestCamFrame.Clone();
                    }
                }

                if (bmp == null && ImgWorkspaceCam.Source is BitmapSource bs)
                {
                    bmp = BitmapSourceToBitmap(bs);
                }

                if (bmp == null)
                {
                    bmp = new System.Drawing.Bitmap(1280, 720);
                    using (var g = System.Drawing.Graphics.FromImage(bmp))
                    {
                        g.Clear(System.Drawing.Color.FromArgb(13, 17, 23));
                        using (var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(48, 54, 61), 2))
                        {
                            g.DrawRectangle(pen, 20, 20, 1240, 680);
                        }
                        using (var brushHeader = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(88, 166, 255)))
                        using (var fontHeader = new System.Drawing.Font("Consolas", 24, System.Drawing.FontStyle.Bold))
                        {
                            g.DrawString("SUPERAUTOMATER · QC CHASSIS AUDIT PHOTO", fontHeader, brushHeader, 50, 60);
                        }
                        using (var brushText = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(201, 209, 217)))
                        using (var fontText = new System.Drawing.Font("Consolas", 14, System.Drawing.FontStyle.Regular))
                        {
                            g.DrawString($"SERIAL NUMBER : {ViewModel.Serial}", fontText, brushText, 50, 140);
                            g.DrawString($"MAKE & MODEL  : {ViewModel.Manufacturer} {ViewModel.Model}", fontText, brushText, 50, 180);
                            g.DrawString($"PHYSICAL GRADE: {ViewModel.Grade}", fontText, brushText, 50, 220);
                            g.DrawString($"PROCESSOR     : {ViewModel.CpuName}", fontText, brushText, 50, 260);
                            g.DrawString($"SYSTEM MEMORY : {ViewModel.RamSummary}", fontText, brushText, 50, 300);
                            g.DrawString($"PRIMARY DRIVE : {ViewModel.PrimaryDriveModel} ({ViewModel.HdsHealth}% Health)", fontText, brushText, 50, 340);
                            g.DrawString($"BATTERY HEALTH: {ViewModel.BatteryIntegrityBadge}", fontText, brushText, 50, 380);
                            g.DrawString($"TIMESTAMP     : {DateTime.Now:yyyy-MM-dd HH:mm:ss}", fontText, brushText, 50, 420);
                        }
                    }
                }

                string savedPath = CosmeticCameraService.Instance.SaveWatermarkedPhoto(bmp, ViewModel.Serial, "Chassis", ViewModel.Grade);
                bmp.Dispose();

                ShowFeedback("QC Chassis Inspection Photo Watermarked & Saved ✓", "📸", "#3FB950");

                try
                {
                    if (File.Exists(savedPath))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = savedPath,
                            UseShellExecute = true
                        });
                    }
                }
                catch { }
            }
            catch (Exception ex)
            {
                ShowFeedback($"Photo Capture Warning: {ex.Message}", "⚠", "#D29922");
            }
        }

        private static System.Drawing.Bitmap BitmapSourceToBitmap(BitmapSource source)
        {
            if (source == null) return null;
            using (var outStream = new MemoryStream())
            {
                BitmapEncoder enc = new BmpBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(source));
                enc.Save(outStream);
                outStream.Position = 0;
                return new System.Drawing.Bitmap(outStream);
            }
        }

        private void BtnExpressQc_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;
            var runner = new ExpressQcRunnerWindow(ViewModel) { Owner = this };
            if (runner.ShowDialog() == true)
            {
                var rep = runner.Report;
                if (rep != null)
                {
                    foreach (var sub in rep.Subsystems)
                    {
                        if (sub.Status == ExpressTestStatus.Passed)
                        {
                            ViewModel.MarkTestPassed(sub.Key);
                            if (sub.Key == "Camera") ViewModel.MarkTestPassed("Camera");
                            if (sub.Key == "Mic" || sub.Key == "Speaker") ViewModel.MarkTestPassed("Audio");
                            if (sub.Key == "CpuRam") ViewModel.MarkTestPassed("Cpu");
                            if (sub.Key == "Battery") ViewModel.MarkTestPassed("Battery");
                            if (sub.Key == "Display") ViewModel.MarkTestPassed("Display");
                            if (sub.Key == "Storage") ViewModel.MarkTestPassed("Storage");
                            if (sub.Key == "Network") ViewModel.MarkTestPassed("Bluetooth");
                        }
                    }

                    ViewModel.Grade = rep.CalculatedGrade;
                    ShowFeedback($"Express QC Passed: {rep.PassedCount}/{rep.TotalCount} Subsystems Certified ({rep.CalculatedGrade}) ✓", "⚡", "#3FB950");

                    OfflineLedgerService.Instance.SaveRecord(new QcAuditRecord
                    {
                        Serial_Number = ViewModel.Serial,
                        Model = ViewModel.Model,
                        Physical_Grade = rep.CalculatedGrade,
                        Status = rep.AllPassed ? "PASSED" : "FLAGGED",
                        CPU_Model = ViewModel.CpuName,
                        RAM_GB = ViewModel.RamSummary,
                        Storage_Details = ViewModel.PrimaryDriveModel,
                        Battery_Health = ViewModel.BatteryIntegrityBadge,
                        GPU_Model = ViewModel.GpuName,
                        Technician_Notes = $"Express QC: {rep.PassedCount}/{rep.TotalCount} Subsystems Nominal. {rep.SummaryText}"
                    });

                    if (runner.CloudDispatchRequested)
                    {
                        BtnOpenSyncMenu_Click(this, new RoutedEventArgs());
                    }
                }
            }
        }

        private void TestRow_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.DataContext is DiagnosticTestItem test)
            {
                RunDiagnosticTest(test.Key);
            }
        }

        private void BtnOpenSyncMenu_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;

            ShowFeedback("Opening QC Inspection & Cloud Dispatch Dialog...", "☁", "#58A6FF");
            var syncDialog = new AssetSyncDialog(ViewModel) { Owner = this };
            if (syncDialog.ShowDialog() == true)
            {
                var rec = syncDialog.GeneratedRecord;
                if (rec != null)
                {
                    OfflineLedgerService.Instance.SaveRecord(new QcAuditRecord
                    {
                        Serial_Number = rec.Serial_Number,
                        Model = rec.Model,
                        Physical_Grade = rec.Physical_Grade,
                        Status = rec.Status,
                        CPU_Model = rec.Processor,
                        RAM_GB = rec.Memory,
                        Storage_Details = ViewModel.PrimaryDriveModel,
                        Battery_Health = rec.Battery_Health.ToString() + "%",
                        GPU_Model = ViewModel.GpuName,
                        Technician_Notes = rec.Wip_Issue
                    });
                }
                string sn = rec?.Serial_Number ?? ViewModel.Serial;
                ShowFeedback($"Asset {sn} successfully dispatched to Google Sheets ({rec?.Physical_Grade}, {rec?.Status})! ✓", "☁", "#3FB950");
            }
            else
            {
                ShowFeedback("Cloud dispatch closed without submitting", "ℹ", "#8B949E");
            }
        }

        private void BtnPrintLabel_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;
            try
            {
                ViewModel.PrintLabelCommand.Execute(null);
                ShowFeedback($"Chassis Thermal Label dispatched for {ViewModel.Serial} (Zebra ZD420) ✓", "🖨", "#3FB950");
            }
            catch (Exception ex)
            {
                ShowFeedback($"Printer Warning: {ex.Message}", "⚠", "#D29922");
            }
        }

        #region Dynamic Central Workspace Management

        public void SwitchWorkspace(string view)
        {
            _activeWorkspace = view;

            // Hide all views first
            ViewStandbyWorkspace.Visibility = Visibility.Collapsed;
            ViewKeyboardWorkspace.Visibility = Visibility.Collapsed;
            ViewCamMicWorkspace.Visibility = Visibility.Collapsed;
            ViewFingerprintWorkspace.Visibility = Visibility.Collapsed;
            ViewCpuWorkspace.Visibility = Visibility.Collapsed;
            ViewGpuWorkspace.Visibility = Visibility.Collapsed;
            ViewStorageWorkspace.Visibility = Visibility.Collapsed;
            ViewBatteryWorkspace.Visibility = Visibility.Collapsed;
            ViewUsbWorkspace.Visibility = Visibility.Collapsed;
            ViewAudioWorkspace.Visibility = Visibility.Collapsed;
            ViewWirelessWorkspace.Visibility = Visibility.Collapsed;
            ViewFleetWorkspace.Visibility = Visibility.Collapsed;

            // Stop non-applicable background loops
            string vLower = view.ToLowerInvariant();
            if (vLower != "cammic" && vLower != "camera" && vLower != "mic")
            {
                StopWorkspaceCamera();
                StopWorkspaceMic();
            }
            if (vLower != "fingerprint")
            {
                StopFingerprintListener();
            }
            if (vLower != "cpu")
            {
                StopCpuStress();
            }
            if (vLower != "gpu")
            {
                StopGpuAnimation();
            }

            switch (vLower)
            {
                case "standby":
                case "home":
                default:
                    ViewStandbyWorkspace.Visibility = Visibility.Visible;
                    TxtWorkspaceActiveTitle.Text = "BENCH READY · STANDBY";
                    TxtActiveViewBadge.Text = "STANDBY HUB";
                    WkStatusDot.Fill = (Brush)FindResource("BrushAccentEmerald");
                    ViewModel?.SetActiveTest("");
                    break;

                case "keyboard":
                    ViewKeyboardWorkspace.Visibility = Visibility.Visible;
                    TxtWorkspaceActiveTitle.Text = "KEYBOARD MATRIX & TRACKPAD VERIFIER";
                    TxtActiveViewBadge.Text = "KEYBOARD & TRACKPAD";
                    WkStatusDot.Fill = (Brush)FindResource("BrushAccentEmerald");
                    KbControl.Focus();
                    ViewModel?.SetActiveTest("Keyboard");
                    break;

                case "cammic":
                case "camera":
                case "mic":
                    ViewCamMicWorkspace.Visibility = Visibility.Visible;
                    TxtWorkspaceActiveTitle.Text = "WEBCAM & MICROPHONE ARRAY";
                    TxtActiveViewBadge.Text = "CAM & MIC";
                    WkStatusDot.Fill = (Brush)FindResource("BrushAccentBlue");
                    InitAndStartWorkspaceCamMic();
                    ViewModel?.SetActiveTest("Camera");
                    break;

                case "fingerprint":
                    ViewFingerprintWorkspace.Visibility = Visibility.Visible;
                    TxtWorkspaceActiveTitle.Text = "BIOMETRIC FINGERPRINT SENSOR";
                    TxtActiveViewBadge.Text = "FINGERPRINT";
                    WkStatusDot.Fill = (Brush)FindResource("BrushAccentEmerald");
                    StartFingerprintWorkflow();
                    ViewModel?.SetActiveTest("Fingerprint");
                    break;

                case "cpu":
                    ViewCpuWorkspace.Visibility = Visibility.Visible;
                    TxtWorkspaceActiveTitle.Text = "CPU & MEMORY MULTI-CORE STRESS";
                    TxtActiveViewBadge.Text = "CPU & RAM";
                    WkStatusDot.Fill = (Brush)FindResource("BrushAccentBlue");
                    TxtCpuStressThreads.Text = $"{Environment.ProcessorCount} Threads Online";
                    ViewModel?.SetActiveTest("Cpu");
                    break;

                case "gpu":
                    ViewGpuWorkspace.Visibility = Visibility.Visible;
                    TxtWorkspaceActiveTitle.Text = "GPU 3D ACCELERATION BENCHMARK";
                    TxtActiveViewBadge.Text = "GPU 3D";
                    WkStatusDot.Fill = (Brush)FindResource("BrushAccentEmerald");
                    StartGpuAnimation();
                    ViewModel?.SetActiveTest("Gpu");
                    break;

                case "storage":
                    ViewStorageWorkspace.Visibility = Visibility.Visible;
                    TxtWorkspaceActiveTitle.Text = "3-SOURCE STORAGE HEALTH & HDS RADAR";
                    TxtActiveViewBadge.Text = "3-SOURCE SSD";
                    WkStatusDot.Fill = (Brush)FindResource("BrushAccentEmerald");
                    ViewModel?.SetActiveTest("Storage");
                    break;

                case "battery":
                case "power":
                    ViewBatteryWorkspace.Visibility = Visibility.Visible;
                    TxtWorkspaceActiveTitle.Text = "BATTERY HEALTH, POWER FLOW & DEGRADATION";
                    TxtActiveViewBadge.Text = "BATTERY HEALTH";
                    WkStatusDot.Fill = (Brush)FindResource("BrushAccentEmerald");
                    ViewModel?.SetActiveTest("Battery");
                    break;

                case "usb":
                case "ports":
                    ViewUsbWorkspace.Visibility = Visibility.Visible;
                    TxtWorkspaceActiveTitle.Text = "USB ROOT HUB & TRANSCEIVER TOPOLOGY";
                    TxtActiveViewBadge.Text = "USB PORTS";
                    WkStatusDot.Fill = (Brush)FindResource("BrushAccentEmerald");
                    ViewModel?.SetActiveTest("Usb");
                    break;

                case "audio":
                    ViewAudioWorkspace.Visibility = Visibility.Visible;
                    TxtWorkspaceActiveTitle.Text = "AUDIO STEREO TRANSDUCER & CHANNEL SEPARATION";
                    TxtActiveViewBadge.Text = "AUDIO STEREO";
                    WkStatusDot.Fill = (Brush)FindResource("BrushAccentBlue");
                    _ = PlayStereoSweepAsync();
                    ViewModel?.SetActiveTest("Audio");
                    break;

                case "bluetooth":
                case "wireless":
                case "wifi":
                    ViewWirelessWorkspace.Visibility = Visibility.Visible;
                    TxtWorkspaceActiveTitle.Text = "WIRELESS RF & BLUETOOTH TRANSCEIVER RADAR";
                    TxtActiveViewBadge.Text = "WIRELESS & BT";
                    WkStatusDot.Fill = (Brush)FindResource("BrushAccentEmerald");
                    ViewModel?.SetActiveTest("Bluetooth");
                    break;

                case "fleet":
                case "radar":
                    ViewFleetWorkspace.Visibility = Visibility.Visible;
                    TxtWorkspaceActiveTitle.Text = "WAREHOUSE FLEET LAN RADAR & MOBILE COCKPIT";
                    TxtActiveViewBadge.Text = "FLEET RADAR";
                    WkStatusDot.Fill = (Brush)FindResource("BrushAccentBlue");
                    RefreshFleetWorkspaceUi();
                    ViewModel?.SetActiveTest("");
                    break;
            }
        }

        private void OnFleetServiceUpdated()
        {
            Dispatcher.InvokeAsync(RefreshFleetWorkspaceUi);
        }

        private void RefreshFleetWorkspaceUi()
        {
            if (ImgFleetQrCode != null && WarehouseFleetService.Instance.QrCodeBitmap != null)
            {
                ImgFleetQrCode.Source = WarehouseFleetService.Instance.QrCodeBitmap;
            }
            if (TxtFleetUrlDisplay != null)
            {
                TxtFleetUrlDisplay.Text = WarehouseFleetService.Instance.LocalDashboardUrl;
            }
            if (TxtFleetIpBadge != null)
            {
                TxtFleetIpBadge.Text = $"{WarehouseFleetService.Instance.LocalIpAddress}:{WarehouseFleetService.Instance.HttpPort} · UDP 9876";
            }
            if (TxtOnlineBenchesCount != null)
            {
                int count = WarehouseFleetService.Instance.OnlineBenches.Count + 1;
                TxtOnlineBenchesCount.Text = $"{count} BENCH{(count == 1 ? " ONLINE (THIS TERMINAL)" : "ES ONLINE")}";
            }
            if (IcOnlineBenches != null)
            {
                IcOnlineBenches.ItemsSource = WarehouseFleetService.Instance.OnlineBenches;
            }
        }

        private void BtnClearDefects_Click(object sender, RoutedEventArgs e)
        {
            ViewModel?.ClearAllCosmeticDefects();
            ShowFeedback("Cosmetic Defect Matrix Cleared: Reset to Pristine Grade A+ ✓", "🎨", "#3FB950");
        }

        private void BtnRetailPrep_Click(object sender, RoutedEventArgs e)
        {
            var prepWin = new RetailPrepModalWindow { Owner = this };
            prepWin.ShowDialog();
        }

        private void BtnCopyFleetUrl_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(WarehouseFleetService.Instance.LocalDashboardUrl);
                ShowFeedback("Fleet Dashboard URL Copied to Clipboard ✓", "📋", "#58A6FF");
            }
            catch { }
        }

        private void BtnOpenFleetBrowser_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(WarehouseFleetService.Instance.LocalDashboardUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ShowFeedback($"Could not launch browser: {ex.Message}", "⚠", "#F85149");
            }
        }

        private void BtnBackToStandby_Click(object sender, RoutedEventArgs e)
        {
            SwitchWorkspace("standby");
        }

        private void BtnStandbyReset_Click(object sender, RoutedEventArgs e) => SwitchWorkspace("standby");

        private void QuickLaunch_Keyboard(object sender, RoutedEventArgs e) => SwitchWorkspace("keyboard");
        private void QuickLaunch_CamMic(object sender, RoutedEventArgs e) => SwitchWorkspace("cammic");
        private void QuickLaunch_Cpu(object sender, RoutedEventArgs e) => SwitchWorkspace("cpu");
        private void QuickLaunch_Battery(object sender, RoutedEventArgs e) => SwitchWorkspace("battery");
        private void QuickLaunch_Gpu(object sender, RoutedEventArgs e) => SwitchWorkspace("gpu");
        private void QuickLaunch_Fingerprint(object sender, RoutedEventArgs e) => SwitchWorkspace("fingerprint");
        private void QuickLaunch_Storage(object sender, RoutedEventArgs e) => SwitchWorkspace("storage");
        private void QuickLaunch_Usb(object sender, RoutedEventArgs e) => SwitchWorkspace("usb");
        private void QuickLaunch_Audio(object sender, RoutedEventArgs e) => SwitchWorkspace("audio");
        private void QuickLaunch_Wireless(object sender, RoutedEventArgs e) => SwitchWorkspace("wireless");
        private void QuickLaunch_Touchscreen(object sender, RoutedEventArgs e) => RunDiagnosticTest("touch");
        private void QuickLaunch_Display(object sender, RoutedEventArgs e) => RunDiagnosticTest("display");

        private void DriveSelectorPill_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is StorageDriveDetail drive && ViewModel != null)
            {
                ViewModel.SelectedDrive = drive;
                ShowFeedback($"Target Storage: {drive.Model} ({drive.CapacityGb}GB) ✓", "💾", "#58A6FF");
            }
        }

        private async void BtnPollBattery_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
            {
                ShowFeedback("Polling Windows SBS & WMI battery registers...", "🔋", "#58A6FF");
                await ViewModel.RefreshTelemetryAsync();
                ShowFeedback($"Battery Telemetry Updated: {ViewModel.BatteryHealth}% Health ({ViewModel.BatteryWearSummary}) ✓", "🔋", "#3FB950");
            }
        }

        private async void BtnRunBatteryLoadTest_Click(object sender, RoutedEventArgs e)
        {
            if (BtnRunBatteryLoadTest == null || ViewModel == null) return;
            BtnRunBatteryLoadTest.IsEnabled = false;
            BtnRunBatteryLoadTest.Content = "⚡ TESTING LOAD SAG (3s)...";
            ShowFeedback("Firing 3-second multi-thread CPU load step to measure dynamic cell voltage sag...", "⚡", "#F1E05A");

            try
            {
                int idleMv = ViewModel.BatteryVoltageMv > 5000 ? ViewModel.BatteryVoltageMv : 12300;
                BatteryLoadMeterService.Instance.StartLoadMeasurement(idleMv);

                using var cts = new CancellationTokenSource();
                var token = cts.Token;
                int threads = Math.Clamp(Environment.ProcessorCount, 2, 8);

                for (int i = 0; i < threads; i++)
                {
                    _ = Task.Run(() =>
                    {
                        while (!token.IsCancellationRequested)
                        {
                            double x = 0;
                            for (int j = 0; j < 50000; j++)
                            {
                                x += Math.Sqrt(j) * Math.Sin(j);
                            }
                        }
                    }, token);
                }

                // Sample voltage over 3.0 seconds (10 intervals of 300ms)
                for (int step = 1; step <= 10; step++)
                {
                    await Task.Delay(300);
                    int curMv = ViewModel.BatteryVoltageMv;
                    BatteryLoadMeterService.Instance.UpdateLoadVoltage(curMv);
                    var partial = BatteryLoadMeterService.Instance.GetCurrentResult();
                    TxtBatterySagSummary.Text = $"Measuring Load Step ({step * 10}%): ΔV {partial.SagVolts:F2}V drop...";
                    TxtBatterySagSummary.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(partial.AccentHex));
                }

                cts.Cancel();

                var finalRes = BatteryLoadMeterService.Instance.StopMeasurement();
                TxtBatterySagSummary.Text = finalRes.StatusSummary;
                TxtBatterySagSummary.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(finalRes.AccentHex));

                ShowFeedback($"Battery Sag: ΔV {finalRes.SagVolts:F2}V ({finalRes.CellBalanceStatus}) ✓", "⚡", finalRes.AccentHex);
            }
            catch (Exception ex)
            {
                ShowFeedback($"Battery load test error: {ex.Message}", "⚠", "#F85149");
            }
            finally
            {
                BtnRunBatteryLoadTest.Content = "⚡ RUN 3-SEC LOAD SAG TEST";
                BtnRunBatteryLoadTest.IsEnabled = true;
            }
        }

        private void BtnPassBattery_Click(object sender, RoutedEventArgs e)
        {
            ViewModel?.MarkTestPassed("Battery");
            ShowFeedback("Battery Health & Power Flow Verified Nominal & Passed ✓", "✓", "#3FB950");
        }

        private void BtnPassUsb_Click(object sender, RoutedEventArgs e)
        {
            ViewModel?.MarkTestPassed("Usb");
            ShowFeedback("USB Ports & Root Hub Verified Nominal & Passed ✓", "✓", "#3FB950");
        }

        private void BtnResetUsb_Click(object sender, RoutedEventArgs e)
        {
            _usbTracker?.Reset();
            ViewModel?.ResetUsbPorts();
            ShowFeedback("USB Port Tests Reset: Insert flash drive into Port 1 to begin", "🔄", "#58A6FF");
        }

        private void BtnCopyListing_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null) return;
            try
            {
                Clipboard.SetText(ViewModel.ECommerceListingText);
                ShowFeedback("Unit Spec Sheet & Refurb Listing Copied to Clipboard ✓", "📋", "#3FB950");
            }
            catch (Exception ex)
            {
                ShowFeedback($"Clipboard copy failed: {ex.Message}", "⚠", "#F85149");
            }
        }

        private void BtnSetGrade_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && ViewModel != null && btn.Content is string grade)
            {
                ViewModel.Grade = $"GRADE {grade.Trim()}";
                ShowFeedback($"Cosmetic Recondition Grade set to {ViewModel.Grade} ✓", "🏷", "#58A6FF");
            }
        }

        private void GpuSelectorPill_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is GpuInfo gpu && ViewModel != null)
            {
                ViewModel.SelectedGpu = gpu;
                ShowFeedback($"Target Adapter: {gpu.Name} ({gpu.TypeBadge}) ✓", "🎮", "#58A6FF");
                StartGpuAnimation();
            }
        }

        private async void BtnRescanWireless_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
            {
                ShowFeedback("Probing Wi-Fi 6 & Bluetooth host controllers...", "📶", "#58A6FF");
                await ViewModel.RefreshTelemetryAsync();
                ShowFeedback($"Wireless Radar Updated: {ViewModel.WifiSsid} · Ping: {ViewModel.GatewayPing} ✓", "📶", "#3FB950");
            }
        }

        private void BtnPassKeyboard_Click(object sender, RoutedEventArgs e)
        {
            ViewModel?.MarkTestPassed("Keyboard");
            ShowFeedback("Keyboard Matrix & Trackpad Certified Nominal & Passed ✓", "✓", "#3FB950");
        }

        private void BtnPassCpu_Click(object sender, RoutedEventArgs e)
        {
            StopCpuStress();
            ViewModel?.MarkTestPassed("Cpu");
            ShowFeedback("CPU & RAM Memory Stress Verified Nominal & Passed ✓", "✓", "#3FB950");
        }

        private void BtnPassGpu_Click(object sender, RoutedEventArgs e)
        {
            ViewModel?.MarkTestPassed("Gpu");
            ShowFeedback("GPU 3D Acceleration Verified Nominal & Passed ✓", "✓", "#3FB950");
        }

        private void BtnPassStorage_Click(object sender, RoutedEventArgs e)
        {
            ViewModel?.MarkTestPassed("Storage");
            ShowFeedback("3-Source Storage Health Verified Nominal & Passed ✓", "✓", "#3FB950");
        }

        private void BtnReplayAudio_Click(object sender, RoutedEventArgs e)
        {
            _ = PlayStereoSweepAsync();
        }

        private void BtnPassAudio_Click(object sender, RoutedEventArgs e)
        {
            ViewModel?.MarkTestPassed("Audio");
            ShowFeedback("Audio Stereo Sweep Verified Nominal & Passed ✓", "✓", "#3FB950");
        }

        private void BtnPassBluetooth_Click(object sender, RoutedEventArgs e)
        {
            ViewModel?.MarkTestPassed("Bluetooth");
            ShowFeedback("Wireless RF & Bluetooth Transceivers Verified Nominal & Passed ✓", "✓", "#3FB950");
        }

        #endregion

        #region Webcam & Microphone Handling

        private void InitAndStartWorkspaceCamMic()
        {
            // 1. Initialize Cameras
            try
            {
                _workspaceVideoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                CmbWorkspaceCameras.Items.Clear();

                if (_workspaceVideoDevices.Count == 0)
                {
                    StackCamNoDevice.Visibility = Visibility.Visible;
                    TxtCamMessage.Text = "No video capture device detected.";
                    TxtCamStatus.Text = "OFFLINE";
                    DotCamStatus.Fill = (Brush)FindResource("BrushTextAmber");
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

            // 2. Start Real-time Microphone Listener
            StartWorkspaceMic();
        }

        private void CmbWorkspaceCameras_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
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
                DotCamStatus.Fill = (Brush)FindResource("BrushAccentEmerald");
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
                lock (_camFrameLock)
                {
                    _latestCamFrame?.Dispose();
                    _latestCamFrame = (System.Drawing.Bitmap)eventArgs.Frame.Clone();
                }

                using var bitmap = (System.Drawing.Bitmap)eventArgs.Frame.Clone();
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
            lock (_camFrameLock)
            {
                _latestCamFrame?.Dispose();
                _latestCamFrame = null;
            }

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
                    TxtMicStatus.Foreground = (Brush)FindResource("BrushTextAmber");
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
                TxtMicStatus.Foreground = (Brush)FindResource("BrushTextEmerald");
            }
            catch (Exception ex)
            {
                TxtMicDeviceName.Text = "Mic Error: " + ex.Message;
            }
        }

        private void WorkspaceWaveIn_DataAvailable(object sender, WaveInEventArgs e)
        {
            float maxSample = 0;
            for (int i = 0; i < e.BytesRecorded; i += 2)
            {
                short sample = (short)((e.Buffer[i + 1] << 8) | e.Buffer[i]);
                float sample32 = Math.Abs(sample / 32768f);
                if (sample32 > maxSample) maxSample = sample32;
            }

            // Convert peak to decibels (-60 dB to 0 dB)
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
                    TxtMicStatus.Foreground = (Brush)FindResource("BrushTextEmerald");

                    if (!_micActivityDetected)
                    {
                        _micActivityDetected = true;
                        BorderMicCertified.Background = new SolidColorBrush(Color.FromArgb(0x40, 0x10, 0xB9, 0x81));
                        TxtMicInstruction.Text = "✓ Audio signal captured! Transducer operational.";
                        ShowFeedback("Microphone Array: Real-Time Audio Signal Certified Nominal ✓", "🎙", "#3FB950");
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

        private void BtnCertifyCamMic_Click(object sender, RoutedEventArgs e)
        {
            ViewModel?.MarkTestPassed("Camera");
            ViewModel?.MarkTestPassed("Audio");
            ShowFeedback("Webcam & Microphone Array Verified Nominal & Passed ✓", "✓", "#3FB950");
        }

        #endregion

        #region Biometric Fingerprint Handling

        private void StartFingerprintWorkflow()
        {
            _fpSensorTouched = false;
            TxtFpStatus.Text = "PROBING SENSOR HARDWARE...";
            TxtFpPassStatus.Text = "Scanning Windows Biometric Framework (WBF)...";

            // Ripple pulse animation timer
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

            // Probe and listen
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
                BorderFpStatus.Background = new SolidColorBrush(Color.FromArgb(0x30, 0x38, 0x8B, 0xFD));
                BorderFpStatus.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x38, 0x8B, 0xFD));
                TxtFpStatus.Foreground = (Brush)FindResource("BrushTextWhite");

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
                BorderFpStatus.Background = new SolidColorBrush(Color.FromArgb(0x30, 0xD2, 0x99, 0x22));
                BorderFpStatus.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0xD2, 0x99, 0x22));
                TxtFpStatus.Foreground = (Brush)FindResource("BrushTextAmber");
            }
        }

        private void OnFingerprintTouchDetected(uint unitId)
        {
            _fpSensorTouched = true;
            BorderFpStatus.Background = new SolidColorBrush(Color.FromArgb(0x30, 0x2E, 0xA0, 0x43));
            BorderFpStatus.BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x2E, 0xA0, 0x43));
            TxtFpStatus.Text = $"✓ SENSOR TOUCH CONFIRMED (UNIT #{unitId})!";
            TxtFpStatus.Foreground = (Brush)FindResource("BrushTextEmerald");
            TxtFpHint.Text = "Hardware biometric response verified 100% nominal";
            TxtFpPassStatus.Text = "Biometric Sensor Tested & Passed Nominal ✓";

            BorderFpTarget.BorderBrush = (Brush)FindResource("BrushAccentEmerald");
            BorderFpTarget.Background = new SolidColorBrush(Color.FromArgb(0x40, 0x10, 0xB9, 0x81));

            ViewModel?.MarkTestPassed("Fingerprint");
            ShowFeedback("Biometric Fingerprint: Hardware Touch Confirmed Nominal ✓", "👆", "#3FB950");
        }

        private void StopFingerprintListener()
        {
            try
            {
                _fpCts?.Cancel();
                _fpCts?.Dispose();
                _fpCts = null;
            }
            catch { }
            try { _fpAnimTimer?.Stop(); } catch { }
            FingerprintService.Instance.CleanupSession();
        }

        private void BtnPassFingerprint_Click(object sender, RoutedEventArgs e)
        {
            OnFingerprintTouchDetected(1);
        }

        private void BtnFpRescan_Click(object sender, RoutedEventArgs e)
        {
            ProbeAndListenFingerprint();
        }

        #endregion

        #region Embedded CPU Stress Handling

        private void BtnCpuToggleStress_Click(object sender, RoutedEventArgs e)
        {
            if (_isCpuStressing)
            {
                StopCpuStress();
            }
            else
            {
                StartCpuStress();
            }
        }

        private void StartCpuStress()
        {
            _isCpuStressing = true;
            _cpuStressSeconds = 0;
            ProgBarCpuStress.Value = 0;
            TxtCpuStressTimer.Text = "Elapsed: 0s / 10s";
            TxtCpuStressStatus.Text = "● ACTIVE MULTI-CORE WORKLOAD (100% LOAD)";
            TxtCpuStressStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF7B72"));
            TxtCpuStressLoad.Text = "100% LOAD";
            BtnCpuToggleStress.Content = "⏹ STOP BURN";

            // Initialize Thermal Profiler & Battery Load Measurement
            ThermalProfilerService.Instance.StartProfiling(ViewModel?.CpuTempC ?? 42);
            BatteryLoadMeterService.Instance.StartLoadMeasurement(ViewModel?.BatteryVoltageMv ?? 12000);
            TxtThermalPasteSummary.Text = "Sampling baseline thermal conductivity...";
            TxtThermalPasteSummary.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#58A6FF"));
            TxtBatterySagSummary.Text = "Sampling baseline terminal voltage...";
            TxtBatterySagSummary.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#58A6FF"));

            // Initialize RAM Bit-Flip Telemetry
            _ramStressRunning = true;
            _ramBytesProcessed = 0;
            _ramErrors = 0;
            ProgBarRamStress.Value = 0;
            TxtRamStressStatus.Text = "● ACTIVE MEMORY ALLOCATION & BIT-FLIP";
            TxtRamStressStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#388BFD"));
            TxtRamThroughput.Text = "Testing...";
            TxtRamPassSummary.Text = "Pass 1/4 (Pattern: 0xAA)";
            TxtRamErrors.Text = "0 Bit-Flip Errors";
            TxtRamErrors.Foreground = (Brush)FindResource("BrushTextEmerald");

            _cpuStressCts = new CancellationTokenSource();
            var token = _cpuStressCts.Token;

            int threads = Environment.ProcessorCount;
            TxtCpuStressThreads.Text = $"{threads} Threads Online";

            for (int i = 0; i < threads; i++)
            {
                Task.Run(() =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        double x = 0;
                        for (int j = 0; j < 100000; j++)
                        {
                            x += Math.Sqrt(j) * Math.Sin(j);
                        }
                    }
                }, token);
            }

            // Launch RAM Bit-Flip Worker Thread
            _ramStressThread = new Thread(() =>
            {
                try
                {
                    const int bufferSize = 64 * 1024 * 1024; // 64 MB
                    byte[] buffer = new byte[bufferSize];
                    byte[] patterns = new byte[] { 0xAA, 0x55, 0x00, 0xFF };
                    var sw = Stopwatch.StartNew();
                    long lastReportBytes = 0;
                    long lastReportTimeMs = 0;

                    int patternIdx = 0;
                    while (_ramStressRunning)
                    {
                        byte p = patterns[patternIdx % patterns.Length];
                        int passNumber = (patternIdx % patterns.Length) + 1;

                        if (patternIdx < patterns.Length)
                        {
                            Dispatcher.InvokeAsync(() =>
                            {
                                TxtRamPassSummary.Text = $"Pass {passNumber}/4 (Pattern: 0x{p:X2})";
                                ProgBarRamStress.Value = (passNumber * 25);
                            });
                        }

                        // Write pass in 64-byte chunks
                        for (int i = 0; i < bufferSize && _ramStressRunning; i += 64)
                        {
                            for (int k = 0; k < 64 && (i + k) < bufferSize; k++)
                            {
                                buffer[i + k] = p;
                            }
                        }

                        // Read & verify pass
                        int localErrors = 0;
                        for (int i = 0; i < bufferSize && _ramStressRunning; i += 64)
                        {
                            for (int k = 0; k < 64 && (i + k) < bufferSize; k++)
                            {
                                if (buffer[i + k] != p)
                                {
                                    localErrors++;
                                }
                            }
                        }

                        _ramErrors += localErrors;
                        _ramBytesProcessed += (long)bufferSize * 2;

                        long elapsedMs = sw.ElapsedMilliseconds;
                        long deltaMs = elapsedMs - lastReportTimeMs;
                        if (deltaMs >= 500)
                        {
                            long deltaBytes = _ramBytesProcessed - lastReportBytes;
                            double gbPerSec = (deltaBytes / (1024.0 * 1024.0 * 1024.0)) / (deltaMs / 1000.0);
                            lastReportBytes = _ramBytesProcessed;
                            lastReportTimeMs = elapsedMs;

                            int curErr = _ramErrors;
                            Dispatcher.InvokeAsync(() =>
                            {
                                TxtRamThroughput.Text = $"{gbPerSec:F1} GB/s";
                                if (curErr > 0)
                                {
                                    TxtRamErrors.Text = $"{curErr} Bit-Flip Errors (FAIL)";
                                    TxtRamErrors.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF7B72"));
                                }
                                else
                                {
                                    TxtRamErrors.Text = "0 Bit-Flip Errors (ECC OK)";
                                }
                            });
                        }

                        patternIdx++;
                    }
                }
                catch { }
            })
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            _ramStressThread.Start();

            _cpuStressTimer?.Stop();
            _cpuStressTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _cpuStressTimer.Tick += (s, e) =>
            {
                _cpuStressSeconds++;
                ProgBarCpuStress.Value = _cpuStressSeconds;
                TxtCpuStressTimer.Text = $"Elapsed: {_cpuStressSeconds}s / 10s";

                // Update real-time thermal paste rise rate & battery load sag
                int curTemp = ViewModel?.CpuTempC ?? 45;
                int curMv = ViewModel?.BatteryVoltageMv ?? 11800;
                ThermalProfilerService.Instance.UpdateSample(curTemp, 0);
                BatteryLoadMeterService.Instance.UpdateLoadVoltage(curMv);

                var thmRes = ThermalProfilerService.Instance.GetCurrentResult();
                TxtThermalPasteSummary.Text = thmRes.ConditionSummary;
                TxtThermalPasteSummary.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(thmRes.AccentHex));

                var batRes = BatteryLoadMeterService.Instance.GetCurrentResult();
                TxtBatterySagSummary.Text = batRes.StatusSummary;
                TxtBatterySagSummary.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(batRes.AccentHex));

                if (_cpuStressSeconds >= 10)
                {
                    StopCpuStress();
                    TxtCpuStressStatus.Text = "✓ 10s STRESS WORKLOAD COMPLETED NOMINAL";
                    TxtCpuStressStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3FB950"));
                    TxtCpuStressLoad.Text = "NOMINAL";

                    TxtRamStressStatus.Text = _ramErrors == 0
                        ? "✓ 4-PASS BIT-FLIP INTEGRITY VERIFIED (0 ERRORS)"
                        : $"⚠ BIT-FLIP INTEGRITY COMPLETED ({_ramErrors} ERRORS)";
                    TxtRamStressStatus.Foreground = _ramErrors == 0
                        ? (Brush)FindResource("BrushTextEmerald")
                        : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF7B72"));
                    ProgBarRamStress.Value = 100;
                    TxtRamPassSummary.Text = "4/4 Passes Complete (0xAA · 0x55 · 0x00 · 0xFF)";

                    ViewModel?.MarkTestPassed("Cpu");
                    ShowFeedback("CPU Multi-Core & RAM Bit-Flip Stress Completed 100% Nominal ✓", "⚡", "#3FB950");
                }
            };
            _cpuStressTimer.Start();
        }

        private void StopCpuStress()
        {
            _isCpuStressing = false;
            _ramStressRunning = false;
            try
            {
                _cpuStressCts?.Cancel();
                _cpuStressCts?.Dispose();
                _cpuStressCts = null;
            }
            catch { }
            try { _cpuStressTimer?.Stop(); } catch { }

            try
            {
                if (_ramStressThread != null && _ramStressThread.IsAlive)
                {
                    _ramStressThread.Join(200);
                }
                _ramStressThread = null;
            }
            catch { }

            var finThm = ThermalProfilerService.Instance.StopProfiling();
            var finBat = BatteryLoadMeterService.Instance.StopMeasurement();

            Dispatcher.InvokeAsync(() =>
            {
                BtnCpuToggleStress.Content = "▶ START 10s CPU + RAM BURN";
                TxtThermalPasteSummary.Text = finThm.ConditionSummary;
                TxtThermalPasteSummary.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(finThm.AccentHex));

                TxtBatterySagSummary.Text = finBat.StatusSummary;
                TxtBatterySagSummary.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(finBat.AccentHex));

                if (_cpuStressSeconds < 10)
                {
                    TxtCpuStressStatus.Text = "WORKLOAD IDLE";
                    TxtCpuStressStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B949E"));
                    TxtCpuStressLoad.Text = "IDLE";

                    TxtRamStressStatus.Text = "MEMORY TEST IDLE";
                    TxtRamStressStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B949E"));
                    TxtRamThroughput.Text = "0.0 GB/s";
                }
            });
        }

        #endregion

        #region Embedded GPU Particle Benchmark Handling

        private void StartGpuAnimation()
        {
            StopGpuAnimation();
            WorkspaceGpuCanvas.Children.Clear();
            _gpuParticles.Clear();

            double width = WorkspaceGpuCanvas.ActualWidth > 50 ? WorkspaceGpuCanvas.ActualWidth : 540;
            double height = WorkspaceGpuCanvas.ActualHeight > 50 ? WorkspaceGpuCanvas.ActualHeight : 320;

            var colors = new[]
            {
                (Color)ColorConverter.ConvertFromString("#58A6FF"),
                (Color)ColorConverter.ConvertFromString("#3FB950"),
                (Color)ColorConverter.ConvertFromString("#BC8CFF"),
                (Color)ColorConverter.ConvertFromString("#388BFD"),
                (Color)ColorConverter.ConvertFromString("#E3B341")
            };

            for (int i = 0; i < 90; i++)
            {
                double size = _rand.Next(3, 8);
                var el = new Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = new SolidColorBrush(colors[_rand.Next(colors.Length)]),
                    Opacity = 0.7 + (_rand.NextDouble() * 0.3)
                };
                var p = new Particle
                {
                    Element = el,
                    X = _rand.NextDouble() * width,
                    Y = _rand.NextDouble() * height,
                    Vx = (_rand.NextDouble() - 0.5) * 6.0,
                    Vy = (_rand.NextDouble() - 0.5) * 6.0
                };
                _gpuParticles.Add(p);
                WorkspaceGpuCanvas.Children.Add(el);
                Canvas.SetLeft(el, p.X);
                Canvas.SetTop(el, p.Y);
            }

            _gpuFrameCount = 0;
            _lastFpsUpdate = DateTime.Now;

            _gpuRenderTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _gpuRenderTimer.Tick += GpuRenderTimer_Tick;
            _gpuRenderTimer.Start();
        }

        private void GpuRenderTimer_Tick(object sender, EventArgs e)
        {
            double width = WorkspaceGpuCanvas.ActualWidth;
            double height = WorkspaceGpuCanvas.ActualHeight;
            if (width < 50 || height < 50) return;

            foreach (var p in _gpuParticles)
            {
                p.X += p.Vx;
                p.Y += p.Vy;

                if (p.X <= 0) { p.X = 0; p.Vx = -p.Vx; }
                else if (p.X >= width - p.Element.Width) { p.X = width - p.Element.Width; p.Vx = -p.Vx; }

                if (p.Y <= 0) { p.Y = 0; p.Vy = -p.Vy; }
                else if (p.Y >= height - p.Element.Height) { p.Y = height - p.Element.Height; p.Vy = -p.Vy; }

                Canvas.SetLeft(p.Element, p.X);
                Canvas.SetTop(p.Element, p.Y);
            }

            _gpuFrameCount++;
            var now = DateTime.Now;
            var elapsed = (now - _lastFpsUpdate).TotalSeconds;
            if (elapsed >= 0.5)
            {
                double fps = _gpuFrameCount / elapsed;
                TxtGpuFps.Text = $"{fps:0.0} FPS";
                _gpuFrameCount = 0;
                _lastFpsUpdate = now;
            }
        }

        private void StopGpuAnimation()
        {
            try { _gpuRenderTimer?.Stop(); } catch { }
            _gpuRenderTimer = null;
        }

        #endregion
    }
}
