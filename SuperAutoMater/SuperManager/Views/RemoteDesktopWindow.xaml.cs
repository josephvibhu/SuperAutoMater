using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using SuperManager.Models;
using SuperManager.Services;

namespace SuperManager.Views
{
    public partial class RemoteDesktopWindow : Window
    {
        private readonly BenchDevice _bench;
        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        private CancellationTokenSource _cts;
        private int _framesReceived = 0;
        private DateTime _lastFpsCalc = DateTime.UtcNow;

        public RemoteDesktopWindow(BenchDevice bench)
        {
            InitializeComponent();
            _bench = bench ?? throw new ArgumentNullException(nameof(bench));

            TxtMachineName.Text = _bench.MachineName;
            TxtIpPort.Text = $"{_bench.IpAddress}:{_bench.Port}";
            Title = $"Remote Desktop — {_bench.MachineName} ({_bench.IpAddress})";

            Loaded += OnLoaded;
            Closing += OnClosing;
            PreviewKeyDown += OnPreviewKeyDown;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _cts = new CancellationTokenSource();
            TxtOverlayHint.Text = $"Connecting to http://{_bench.IpAddress}:{_bench.Port}...";
            Task.Run(() => StreamLoopAsync(_cts.Token));
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        private async Task StreamLoopAsync(CancellationToken ct)
        {
            string frameUrl = $"http://{_bench.IpAddress}:{_bench.Port}/api/remote/frame";
            if (!string.IsNullOrWhiteSpace(_bench.SessionAccessToken))
                frameUrl += $"?token={Uri.EscapeDataString(_bench.SessionAccessToken)}";

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var response = await _http.GetAsync(frameUrl, ct);
                    if (response.IsSuccessStatusCode)
                    {
                        byte[] bytes = await response.Content.ReadAsByteArrayAsync(ct);
                        if (bytes.Length > 0)
                        {
                            await Dispatcher.InvokeAsync(() =>
                            {
                                try
                                {
                                    var bmp = new BitmapImage();
                                    using (var ms = new MemoryStream(bytes))
                                    {
                                        bmp.BeginInit();
                                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                                        bmp.StreamSource = ms;
                                        bmp.EndInit();
                                        bmp.Freeze();
                                    }
                                    ImgScreen.Source = bmp;

                                    if (OverlayConnecting.Visibility == Visibility.Visible)
                                        OverlayConnecting.Visibility = Visibility.Collapsed;

                                    _framesReceived++;
                                    var now = DateTime.UtcNow;
                                    var elapsed = (now - _lastFpsCalc).TotalSeconds;
                                    if (elapsed >= 1.0)
                                    {
                                        double fps = _framesReceived / elapsed;
                                        TxtFps.Text = $"{fps:F0} FPS";
                                        _framesReceived = 0;
                                        _lastFpsCalc = now;
                                    }
                                }
                                catch { }
                            });
                        }
                    }
                    else
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            TxtStatus.Text = $"⚠️ Server returned {response.StatusCode}";
                            TxtStatus.Foreground = System.Windows.Media.Brushes.Orange;
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        TxtStatus.Text = $"❌ Reconnecting... ({ex.Message})";
                        TxtStatus.Foreground = System.Windows.Media.Brushes.Red;
                    });
                    await Task.Delay(1000, ct);
                }

                await Task.Delay(40, ct);
            }
        }

        #region Mouse & Keyboard Forwarding

        private (double xRatio, double yRatio)? GetNormalizedCoordinates(MouseEventArgs e)
        {
            if (ImgScreen.Source == null || ImgScreen.ActualWidth <= 0 || ImgScreen.ActualHeight <= 0)
                return null;

            var pos = e.GetPosition(ImgScreen);

            double imgW = ImgScreen.Source.Width;
            double imgH = ImgScreen.Source.Height;
            double scale = Math.Min(ImgScreen.ActualWidth / imgW, ImgScreen.ActualHeight / imgH);
            double renderW = imgW * scale;
            double renderH = imgH * scale;

            double offsetX = (ImgScreen.ActualWidth - renderW) / 2.0;
            double offsetY = (ImgScreen.ActualHeight - renderH) / 2.0;

            if (pos.X < offsetX || pos.X > offsetX + renderW ||
                pos.Y < offsetY || pos.Y > offsetY + renderH)
            {
                return null;
            }

            double xRatio = (pos.X - offsetX) / renderW;
            double yRatio = (pos.Y - offsetY) / renderH;

            return (Math.Clamp(xRatio, 0.0, 1.0), Math.Clamp(yRatio, 0.0, 1.0));
        }

        private void SendRemoteInput(object payload)
        {
            if (ChkInteractive?.IsChecked != true) return;

            Task.Run(async () =>
            {
                try
                {
                    string url = $"http://{_bench.IpAddress}:{_bench.Port}/api/remote/input";
                    if (!string.IsNullOrWhiteSpace(_bench.SessionAccessToken))
                        url += $"?token={Uri.EscapeDataString(_bench.SessionAccessToken)}";

                    string json = JsonSerializer.Serialize(payload);
                    using var content = new StringContent(json, Encoding.UTF8, "application/json");
                    await _http.PostAsync(url, content);
                }
                catch { }
            });
        }

        private void ImgScreen_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var coords = GetNormalizedCoordinates(e);
            if (!coords.HasValue) return;

            string action = e.ChangedButton == MouseButton.Right ? "right_click" : "left_click";
            if (e.ClickCount >= 2 && e.ChangedButton == MouseButton.Left)
                action = "double_click";

            SendRemoteInput(new { type = "mouse", action = action, x = coords.Value.xRatio, y = coords.Value.yRatio });
            e.Handled = true;
        }

        private void ImgScreen_MouseUp(object sender, MouseButtonEventArgs e)
        {
            var coords = GetNormalizedCoordinates(e);
            if (!coords.HasValue) return;

            string action = e.ChangedButton == MouseButton.Right ? "right_up" : "left_up";
            SendRemoteInput(new { type = "mouse", action = action, x = coords.Value.xRatio, y = coords.Value.yRatio });
            e.Handled = true;
        }

        private void ImgScreen_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed || e.RightButton == MouseButtonState.Pressed)
            {
                var coords = GetNormalizedCoordinates(e);
                if (coords.HasValue)
                {
                    SendRemoteInput(new { type = "mouse", action = "move", x = coords.Value.xRatio, y = coords.Value.yRatio });
                }
            }
        }

        private void ImgScreen_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            var coords = GetNormalizedCoordinates(e);
            double x = coords?.xRatio ?? 0.5;
            double y = coords?.yRatio ?? 0.5;

            SendRemoteInput(new { type = "mouse", action = "wheel", x = x, y = y, delta = e.Delta });
            e.Handled = true;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (ChkInteractive?.IsChecked != true) return;

            if (e.Key == Key.System && e.SystemKey == Key.F4) return;

            string keyName = e.Key switch
            {
                Key.Enter => "Enter",
                Key.Escape => "Escape",
                Key.Space => "Space",
                Key.Tab => "Tab",
                Key.Back => "Backspace",
                Key.Delete => "Delete",
                Key.Up => "Up",
                Key.Down => "Down",
                Key.Left => "Left",
                Key.Right => "Right",
                Key.F5 => "F5",
                Key.F11 => "F11",
                Key.LWin or Key.RWin => "Win",
                _ => e.Key.ToString()
            };

            SendRemoteInput(new { type = "key", key = keyName });
            e.Handled = true;
        }

        #endregion

        #region Quick Action Buttons

        private void BtnCmd_Click(object sender, RoutedEventArgs e) =>
            SendRemoteInput(new { type = "action", action = "cmd" });

        private void BtnTaskMgr_Click(object sender, RoutedEventArgs e) =>
            SendRemoteInput(new { type = "action", action = "taskmgr" });

        private void BtnShowDesktop_Click(object sender, RoutedEventArgs e) =>
            SendRemoteInput(new { type = "action", action = "show_desktop" });

        private void BtnEnter_Click(object sender, RoutedEventArgs e) =>
            SendRemoteInput(new { type = "key", key = "Enter" });

        private void BtnEsc_Click(object sender, RoutedEventArgs e) =>
            SendRemoteInput(new { type = "key", key = "Escape" });

        private async void BtnPass_Click(object sender, RoutedEventArgs e)
        {
            bool ok = await FleetCommandService.Instance.SendPassActionAsync(_bench);
            if (ok)
            {
                TxtStatus.Text = "✓ Remote Pass action executed.";
                TxtStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        #endregion
    }
}
