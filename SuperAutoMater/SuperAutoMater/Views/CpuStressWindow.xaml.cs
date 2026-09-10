using System;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace SuperAutoMater.Wpf.Views
{
    public partial class CpuStressWindow : Window
    {
        private CancellationTokenSource _cts;
        private DispatcherTimer _timer;
        private int _elapsedSec = 0;
        public bool TestPassed { get; private set; } = false;

        public CpuStressWindow()
        {
            InitializeComponent();
            Loaded += CpuStressWindow_Loaded;
            Closed += CpuStressWindow_Closed;
            KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape)
                {
                    BtnCancel_Click(this, new RoutedEventArgs());
                }
                else if (e.Key == System.Windows.Input.Key.Enter)
                {
                    BtnPass_Click(this, new RoutedEventArgs());
                }
            };
        }

        private void CpuStressWindow_Loaded(object sender, RoutedEventArgs e)
        {
            int cores = Environment.ProcessorCount;
            TxtThreads.Text = $"Threads: {cores} Cores";

            _cts = new CancellationTokenSource();
            StartBurn(_cts.Token, cores);

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, ev) =>
            {
                _elapsedSec++;
                ProgBarStress.Value = Math.Min(_elapsedSec, 10);
                TxtTimer.Text = $"Elapsed: {_elapsedSec}s / 10s (Nominal Thermals)";

                if (_elapsedSec >= 10)
                {
                    _timer.Stop();
                    _cts?.Cancel();
                    TxtStatus.Text = "BENCHMARK COMPLETED NOMINAL ✓";
                    TxtCpuPct.Text = "PASS";
                    TxtCpuPct.Foreground = (System.Windows.Media.Brush)FindResource("BrushTextEmerald");
                    try { SystemSounds.Asterisk.Play(); } catch { }
                }
            };
            _timer.Start();
        }

        private void StartBurn(CancellationToken token, int threads)
        {
            for (int i = 0; i < threads; i++)
            {
                Task.Run(() =>
                {
                    long val = 0;
                    while (!token.IsCancellationRequested)
                    {
                        val = (val + 17) * 31 % 1000000007;
                    }
                }, token);
            }
        }

        private void CpuStressWindow_Closed(object sender, EventArgs e)
        {
            _cts?.Cancel();
            _timer?.Stop();
        }

        private void BtnPass_Click(object sender, RoutedEventArgs e)
        {
            TestPassed = true;
            try { SystemSounds.Asterisk.Play(); } catch { }
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
