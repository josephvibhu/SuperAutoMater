using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using AForge.Video;
using AForge.Video.DirectShow;
using NAudio.Wave;

namespace ITAS_QC_Tool
{
    public sealed class MicMeterViewModel : IDisposable
    {
        public event Action<float> LevelChanged;
        private WaveInEvent _waveIn;
        private bool _isDisposed = false;
        private DateTime _lastUpdate = DateTime.MinValue;

        public bool IsMicConnected => WaveInEvent.DeviceCount > 0;

        public void Start()
        {
            if (_isDisposed || !IsMicConnected) return;

            try
            {
                _waveIn = new WaveInEvent
                {
                    WaveFormat = new WaveFormat(44100, 16, 1),
                    BufferMilliseconds = 50
                };
                _waveIn.DataAvailable += (s, e) =>
                {
                    if ((DateTime.Now - _lastUpdate).TotalMilliseconds < 40) return;
                    _lastUpdate = DateTime.Now;

                    float peak = 0;
                    for (int i = 0; i < e.BytesRecorded; i += 2)
                    {
                        short sample = (short)((e.Buffer[i + 1] << 8) | e.Buffer[i]);
                        float val = Math.Abs(sample / 32768f);
                        if (val > peak) peak = val;
                    }
                    float scaled = Math.Min(1.0f, peak * 1.5f);
                    LevelChanged?.Invoke(scaled);
                };
                _waveIn.StartRecording();
            }
            catch { }
        }

        public void Stop()
        {
            try
            {
                _waveIn?.StopRecording();
                _waveIn?.Dispose();
                _waveIn = null;
            }
            catch { }
        }

        public void Dispose()
        {
            _isDisposed = true;
            Stop();
        }
    }

    public partial class Form1
    {
        private int selectedCameraIndex = -1;
        private string activeCameraName = "Default Camera";
        private Button btnCameraToggle;
        private Button btnCameraSwitch;

        private VideoCaptureDevice videoSource;
        private PictureBox camView;
        private volatile bool camRunning = false;
        private volatile bool isProcessingFrame = false;
        private MicMeterViewModel micViewModel;

        private bool isCameraInitializing = false;
        private readonly System.Threading.SemaphoreSlim _cameraLock = new System.Threading.SemaphoreSlim(1, 1);

        private int SelectBestCameraIndex(FilterInfoCollection devices)
        {
            if (devices == null || devices.Count == 0) return -1;
            if (selectedCameraIndex >= 0 && selectedCameraIndex < devices.Count)
                return selectedCameraIndex;

            // 1. Prioritize regular color RGB cameras by filtering out IR / Night Vision / Windows Hello
            for (int i = 0; i < devices.Count; i++)
            {
                string name = devices[i].Name ?? "";
                if (!IsIrOrNightVisionCamera(name))
                {
                    selectedCameraIndex = i;
                    return i;
                }
            }

            // 2. Fallback to first camera if all are IR
            selectedCameraIndex = 0;
            return 0;
        }

        internal static bool IsIrOrNightVisionCamera(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            string lower = name.ToLowerInvariant();
            return lower.Contains("ir camera") ||
                   lower.Contains("infrared") ||
                   lower.Contains("night vision") ||
                   lower.Contains("hello face") ||
                   lower.Contains("depth") ||
                   lower.Contains("realsense") ||
                   lower.Contains("ir sensor") ||
                   lower.Contains("rgb-ir") ||
                   lower.Contains("virtual") ||
                   lower.Contains("dmft");
        }

        public async void LaunchCameraTest()
        {
            ToggleWebcam(btnCameraToggle, EventArgs.Empty);
        }

        private async void SwitchCamera()
        {
            try
            {
                FilterInfoCollection devices = await Task.Run(() => new FilterInfoCollection(FilterCategory.VideoInputDevice));
                if (devices.Count <= 1)
                {
                    DarkMessageBox.Show(devices.Count == 1 ? $"Only 1 camera detected:\n\n{devices[0].Name}" : "No cameras detected on this system.", "Camera Switch");
                    return;
                }

                int currentIdx = selectedCameraIndex >= 0 ? selectedCameraIndex : 0;
                int nextIdx = (currentIdx + 1) % devices.Count;
                selectedCameraIndex = nextIdx;
                string newCamName = devices[nextIdx].Name;
                bool isIr = IsIrOrNightVisionCamera(newCamName);
                string tag = isIr ? " [IR / Night Vision]" : " [RGB Standard]";

                DarkMessageBox.Show($"Switched to Camera {nextIdx + 1} of {devices.Count}:\n\n{newCamName}{tag}", "Camera Selected");

                if (camRunning)
                {
                    StopWebcam();
                    await Task.Delay(300);
                    ToggleWebcam(btnCameraToggle, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                DarkMessageBox.Show("Failed to switch camera: " + ex.Message, "Camera Switch Error");
            }
        }

        private async void ToggleWebcam(object sender, EventArgs e)
        {
            if (isCameraInitializing) return;

            if (camRunning)
            {
                await StopWebcamAsync();
                return;
            }

            isCameraInitializing = true;
            if (btnCameraToggle != null)
            {
                btnCameraToggle.Text = "INITIALIZING...";
                btnCameraToggle.Enabled = false;
            }

            try
            {
                await _cameraLock.WaitAsync();
                try
                {
                    await Task.Run(() =>
                    {
                        try
                        {
                            FilterInfoCollection devices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                            if (devices.Count == 0)
                            {
                                this.BeginInvoke(new Action(() =>
                                {
                                    DarkMessageBox.Show("No video input capture devices (webcams) found.", "Webcam Not Detected");
                                }));
                                return;
                            }

                            int bestIdx = SelectBestCameraIndex(devices);
                            activeCameraName = devices[bestIdx].Name;
                            videoSource = new VideoCaptureDevice(devices[bestIdx].MonikerString);

                            // Select highest resolution under 1080p for crisp framerate
                            VideoCapabilities bestCap = null;
                            int bestScore = 0;
                            foreach (var cap in videoSource.VideoCapabilities)
                            {
                                int score = cap.FrameSize.Width * cap.FrameSize.Height;
                                if (cap.FrameSize.Width <= 1920 && cap.FrameSize.Height <= 1080 && score > bestScore)
                                {
                                    bestScore = score;
                                    bestCap = cap;
                                }
                            }
                            if (bestCap != null) videoSource.VideoResolution = bestCap;

                            videoSource.NewFrame += VideoSource_NewFrame;
                            videoSource.Start();
                            camRunning = true;
                            MarkTestComplete("Camera");
                        }
                        catch (Exception ex)
                        {
                            this.BeginInvoke(new Action(() =>
                            {
                                DarkMessageBox.Show($"DirectShow Capture Error:\n\n{ex.Message}", "Camera Error");
                            }));
                        }
                    });
                }
                finally
                {
                    _cameraLock.Release();
                }

                if (camRunning && btnCameraToggle != null)
                {
                    btnCameraToggle.Text = "STOP CAMERA TEST";
                    if (btnCameraToggle is HudButton hb) hb.HudAccentColor = HudTheme.FailWarning;
                }
                else if (btnCameraToggle != null)
                {
                    btnCameraToggle.Text = "START CAMERA TEST";
                    if (btnCameraToggle is HudButton hb) hb.HudAccentColor = HudTheme.HudAccent;
                }
            }
            finally
            {
                isCameraInitializing = false;
                if (btnCameraToggle != null) btnCameraToggle.Enabled = true;
            }
        }

        private async Task StopWebcamAsync()
        {
            if (btnCameraToggle != null)
            {
                btnCameraToggle.Text = "STOPPING...";
                btnCameraToggle.Enabled = false;
            }

            try
            {
                camRunning = false;

                if (videoSource != null)
                {
                    var vs = videoSource;
                    videoSource = null;
                    try { vs.NewFrame -= VideoSource_NewFrame; } catch { }

                    await Task.Run(() =>
                    {
                        try
                        {
                            if (vs != null && vs.IsRunning)
                            {
                                vs.SignalToStop();
                                var stopWait = Task.Run(() => { try { vs.WaitForStop(); } catch { } });
                                Task.WaitAny(new[] { stopWait }, 800); // 800ms hard timeout
                            }
                        }
                        catch { }
                    });
                }

                if (camView != null)
                {
                    var old = camView.Image;
                    camView.Image = null;
                    old?.Dispose();
                    camView.Invalidate();
                }
            }
            finally
            {
                if (btnCameraToggle != null)
                {
                    btnCameraToggle.Text = "START CAMERA TEST";
                    if (btnCameraToggle is HudButton hb) hb.HudAccentColor = HudTheme.HudAccent;
                    btnCameraToggle.Enabled = true;
                    btnCameraToggle.Invalidate();
                }
            }
        }

        private void StopWebcam()
        {
            _ = StopWebcamAsync();
        }

        private void VideoSource_NewFrame(object sender, NewFrameEventArgs eventArgs)
        {
            if (!camRunning || isProcessingFrame) return;
            isProcessingFrame = true;

            try
            {
                Bitmap frame = (Bitmap)eventArgs.Frame.Clone();
                if (this.IsHandleCreated && !this.IsDisposed && camRunning)
                {
                    this.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            if (!camRunning)
                            {
                                frame?.Dispose();
                                return;
                            }
                            var old = camView.Image;
                            camView.Image = frame;
                            old?.Dispose();
                        }
                        catch { }
                        finally
                        {
                            isProcessingFrame = false;
                        }
                    }));
                }
                else
                {
                    frame.Dispose();
                    isProcessingFrame = false;
                }
            }
            catch
            {
                isProcessingFrame = false;
            }
        }

        public void StartMicVisualizer()
        {
            try
            {
                micViewModel = new MicMeterViewModel();
                micViewModel.LevelChanged += (level) =>
                {
                    if (this.IsHandleCreated && !this.IsDisposed)
                    {
                        try
                        {
                            this.BeginInvoke(new Action(() =>
                            {
                                UpdateMicBar(level);
                            }));
                        }
                        catch { }
                    }
                };

                if (micViewModel.IsMicConnected)
                {
                    micViewModel.Start();
                    if (lblMicState != null) lblMicState.Text = "MIC SENSOR : ACTIVE [44.1 kHz / 16-BIT]";
                }
                else
                {
                    if (lblMicState != null) lblMicState.Text = "[ NO MIC DETECTED ]";
                }
            }
            catch
            {
                if (lblMicState != null) lblMicState.Text = "[ NO MIC CONNECTED ]";
            }
        }

        public void StopMicVisualizer()
        {
            try
            {
                micViewModel?.Stop();
                micViewModel?.Dispose();
            }
            catch { }
            finally
            {
                micViewModel = null;
            }
        }

        private Panel BuildCameraPanel()
        {
            Panel outer = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(14, 20, 24), Padding = new Padding(2) };
            outer.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                using (Pen p = new Pen(HudTheme.Bezel, 1))
                    e.Graphics.DrawRectangle(p, 0, 0, outer.Width - 1, outer.Height - 1);
            };

            camView = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(10, 14, 16),
                SizeMode = PictureBoxSizeMode.Zoom,
                Margin = new Padding(0)
            };

            outer.Controls.Add(camView);
            return outer;
        }

        private void UpdateMicBar(float percent)
        {
            if (micVuMeter != null && !micVuMeter.IsDisposed)
            {
                micVuMeter.Level = percent;
            }
        }
    }
}
