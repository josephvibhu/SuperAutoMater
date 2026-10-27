using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using VeloxDeploy.ViewModels;

namespace VeloxDeploy
{
    public partial class MainWindow : Window
    {
        private const int WM_DEVICECHANGE = 0x0219;
        private const int DBT_DEVICEARRIVAL = 0x8000;
        private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;

        public MainWindow()
        {
            InitializeComponent();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            if (PresentationSource.FromVisual(this) is HwndSource hwndSource)
            {
                hwndSource.AddHook(HwndHandler);
            }
        }

        private IntPtr HwndHandler(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_DEVICECHANGE)
            {
                int wp = wParam.ToInt32();
                if (wp == DBT_DEVICEARRIVAL || wp == DBT_DEVICEREMOVECOMPLETE)
                {
                    if (DataContext is DeployViewModel vm && vm.IsIdle)
                    {
                        Dispatcher.InvokeAsync(async () =>
                        {
                            // Debounce slightly to allow the OS to register partition mounts
                            await Task.Delay(1200);
                            vm.ProbeHardware();
                        });
                    }
                }
            }
            return IntPtr.Zero;
        }
    }
}
