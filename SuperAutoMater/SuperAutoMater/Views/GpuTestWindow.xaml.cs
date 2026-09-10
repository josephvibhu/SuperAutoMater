using System;
using System.Diagnostics;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SuperAutoMater.Wpf.ViewModels;

namespace SuperAutoMater.Wpf.Views
{
    public partial class GpuTestWindow : Window
    {
        private struct Particle
        {
            public double X, Y, Z;
            public double Angle;
            public double Speed;
            public double Radius;
            public Ellipse Shape;
        }

        private readonly Particle[] _particles = new Particle[320];
        private readonly Stopwatch _sw = new Stopwatch();
        private int _frameCount = 0;
        private double _lastFpsUpdate = 0;
        public bool TestPassed { get; private set; } = false;

        public GpuTestWindow(MainViewModel vm = null)
        {
            InitializeComponent();

            if (vm != null)
            {
                if (!string.IsNullOrWhiteSpace(vm.GpuName) && vm.GpuName != "Detecting GPU...")
                {
                    TxtGpuName.Text = vm.GpuName;
                }
                if (!string.IsNullOrWhiteSpace(vm.GpuVram))
                {
                    TxtVram.Text = $"VRAM: {vm.GpuVram}";
                }
                if (!string.IsNullOrWhiteSpace(vm.GpuDriver))
                {
                    TxtDriver.Text = $"Driver: {vm.GpuDriver}";
                }
            }

            Loaded += GpuTestWindow_Loaded;
            Closed += GpuTestWindow_Closed;
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    BtnCancel_Click(this, new RoutedEventArgs());
                }
                else if (e.Key == Key.Enter)
                {
                    BtnPass_Click(this, new RoutedEventArgs());
                }
            };
        }

        private void GpuTestWindow_Loaded(object sender, RoutedEventArgs e)
        {
            InitParticles();
            _sw.Start();
            CompositionTarget.Rendering += OnRendering;
        }

        private void InitParticles()
        {
            RenderCanvas.Children.Clear();
            var rand = new Random();
            var colors = new Brush[]
            {
                new SolidColorBrush(Color.FromRgb(56, 139, 253)),
                new SolidColorBrush(Color.FromRgb(46, 160, 67)),
                new SolidColorBrush(Color.FromRgb(210, 153, 34)),
                new SolidColorBrush(Color.FromRgb(88, 166, 255)),
                new SolidColorBrush(Color.FromRgb(240, 246, 252))
            };

            for (int i = 0; i < _particles.Length; i++)
            {
                double size = rand.NextDouble() * 5 + 2;
                var ell = new Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = colors[i % colors.Length],
                    Opacity = 0.8
                };
                RenderCanvas.Children.Add(ell);

                _particles[i] = new Particle
                {
                    Angle = rand.NextDouble() * Math.PI * 2,
                    Speed = 0.015 + rand.NextDouble() * 0.035,
                    Radius = 40 + rand.NextDouble() * 220,
                    Z = rand.NextDouble() * 100 - 50,
                    Shape = ell
                };
            }
        }

        private void OnRendering(object sender, EventArgs e)
        {
            double elapsedSec = _sw.Elapsed.TotalSeconds;
            _frameCount++;

            if (elapsedSec - _lastFpsUpdate >= 0.5)
            {
                double fps = _frameCount / (elapsedSec - _lastFpsUpdate);
                TxtFps.Text = $"{fps:F1} FPS";
                _frameCount = 0;
                _lastFpsUpdate = elapsedSec;
            }

            double cx = RenderCanvas.ActualWidth > 0 ? RenderCanvas.ActualWidth / 2.0 : 360;
            double cy = RenderCanvas.ActualHeight > 0 ? RenderCanvas.ActualHeight / 2.0 : 200;

            for (int i = 0; i < _particles.Length; i++)
            {
                ref var p = ref _particles[i];
                p.Angle += p.Speed;

                // 3D toroidal vortex projection
                double cosA = Math.Cos(p.Angle);
                double sinA = Math.Sin(p.Angle);
                double scale = 1.0 + (p.Z / 120.0);

                double px = cx + (cosA * p.Radius * scale);
                double py = cy + (sinA * (p.Radius * 0.45) * scale);

                Canvas.SetLeft(p.Shape, px);
                Canvas.SetTop(p.Shape, py);
            }
        }

        private void GpuTestWindow_Closed(object sender, EventArgs e)
        {
            CompositionTarget.Rendering -= OnRendering;
            _sw.Stop();
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
