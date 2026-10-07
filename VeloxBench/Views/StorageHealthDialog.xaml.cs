using System;
using System.Media;
using System.Windows;
using System.Windows.Input;
using SuperAutoMater.Wpf.ViewModels;

namespace SuperAutoMater.Wpf.Views
{
    public partial class StorageHealthDialog : Window
    {
        private readonly MainViewModel _vm;

        public StorageHealthDialog(MainViewModel vm = null)
        {
            InitializeComponent();
            _vm = vm;

            if (_vm != null)
            {
                if (!string.IsNullOrWhiteSpace(_vm.PrimaryDriveModel))
                {
                    TxtDriveModel.Text = _vm.PrimaryDriveModel;
                }
                if (!string.IsNullOrWhiteSpace(_vm.StorageSummary))
                {
                    TxtDriveIface.Text = _vm.StorageSummary;
                }
            }

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

        private void BtnPass_Click(object sender, RoutedEventArgs e)
        {
            try { SystemSounds.Asterisk.Play(); } catch { }
            _vm?.MarkTestPassed("Storage");
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
