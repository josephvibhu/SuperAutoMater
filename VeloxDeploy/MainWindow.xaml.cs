using System;
using System.Collections.Specialized;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using VeloxDeploy.ViewModels;

namespace VeloxDeploy
{
    public partial class MainWindow : Window
    {
        private const int WM_DEVICECHANGE = 0x0219;
        private const int DBT_DEVICEARRIVAL = 0x8000;
        private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;

        private ScrollViewer? _logScrollViewer;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            EnsureWindowFitsScreen();
            HookLogAutoScroll();
            var dpd = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemsSourceProperty, typeof(ListBox));
            dpd?.AddValueChanged(LogListBox, (s, args) => HookLogAutoScroll());
        }

        private void EnsureWindowFitsScreen()
        {
            try
            {
                var workArea = SystemParameters.WorkArea;
                if (workArea.Width > 0 && workArea.Height > 0)
                {
                    if (Width > workArea.Width)
                    {
                        Width = Math.Max(MinWidth, workArea.Width - 40);
                    }
                    if (Height > workArea.Height)
                    {
                        Height = Math.Max(MinHeight, workArea.Height - 40);
                    }

                    Left = Math.Max(workArea.Left + 10, (workArea.Width - Width) / 2 + workArea.Left);
                    Top = Math.Max(workArea.Top + 10, (workArea.Height - Height) / 2 + workArea.Top);
                }
            }
            catch
            {
                // Fallback to default centering if DWM query fails
            }
        }

        private void HookLogAutoScroll()
        {
            if (LogListBox.ItemsSource is INotifyCollectionChanged notifySource)
            {
                notifySource.CollectionChanged -= LogListBox_CollectionChanged;
                notifySource.CollectionChanged += LogListBox_CollectionChanged;
            }
            else if (LogListBox.Items is INotifyCollectionChanged notifyCollection)
            {
                notifyCollection.CollectionChanged -= LogListBox_CollectionChanged;
                notifyCollection.CollectionChanged += LogListBox_CollectionChanged;
            }
        }

        private void LogListBox_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (LogListBox.Items.Count > 0)
            {
                Dispatcher.InvokeAsync(() =>
                {
                    _logScrollViewer ??= FindVisualChild<ScrollViewer>(LogListBox);
                    if (_logScrollViewer != null)
                    {
                        _logScrollViewer.ScrollToEnd();
                    }
                    else
                    {
                        LogListBox.ScrollIntoView(LogListBox.Items[LogListBox.Items.Count - 1]);
                    }
                }, DispatcherPriority.Background);
            }
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild) return typedChild;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
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
