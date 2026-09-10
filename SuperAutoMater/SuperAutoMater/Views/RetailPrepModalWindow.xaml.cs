using System;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SuperAutoMater.Wpf.Services;

namespace SuperAutoMater.Wpf.Views
{
    public partial class RetailPrepModalWindow : Window
    {
        private readonly StringBuilder _logBuilder = new StringBuilder();

        public RetailPrepModalWindow()
        {
            InitializeComponent();
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) Close();
            };
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void BtnExecutePrep_Click(object sender, RoutedEventArgs e)
        {
            bool cleanTemp = ChkCleanTemp.IsChecked == true;
            bool emptyRecycle = ChkEmptyRecycle.IsChecked == true;
            bool clearLogs = ChkClearLogs.IsChecked == true;
            bool resetPower = ChkResetPower.IsChecked == true;
            bool triggerOobe = ChkOobeSysprep.IsChecked == true;

            if (triggerOobe)
            {
                var confirm = MessageBox.Show(
                    "ARM CUSTOMER OOBE CONFIRMATION:\n\n" +
                    "This will arm Windows with the Out-Of-Box Experience (OOBE) setup wizard and shut down the computer for customer packaging.\n\n" +
                    "Are you sure you want to seal this laptop now?",
                    "Confirm Retail Packaging Seal",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (confirm != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            BtnExecutePrep.IsEnabled = false;
            ChkCleanTemp.IsEnabled = false;
            ChkEmptyRecycle.IsEnabled = false;
            ChkClearLogs.IsEnabled = false;
            ChkResetPower.IsEnabled = false;
            ChkOobeSysprep.IsEnabled = false;

            _logBuilder.Clear();
            TxtTerminalLog.Text = "";

            EventHandler<RetailPrepProgressEventArgs> handler = (s, args) =>
            {
                Dispatcher.Invoke(() =>
                {
                    PbRetailPrep.Value = args.Percentage;
                    TxtPercent.Text = $"{args.Percentage}%";
                    TxtStatusStage.Text = args.Step;

                    try
                    {
                        var brush = new BrushConverter().ConvertFromString(args.AccentHex) as Brush;
                        if (brush != null) TxtStatusStage.Foreground = brush;
                    }
                    catch { }

                    _logBuilder.AppendLine($"[{DateTime.Now:HH:mm:ss}] [{args.Percentage}%] {args.Step}: {args.Details}");
                    TxtTerminalLog.Text = _logBuilder.ToString();
                    ScrollLogs.ScrollToEnd();
                });
            };

            RetailPrepService.Instance.ProgressUpdated += handler;

            try
            {
                bool success = await RetailPrepService.Instance.RunRetailPrepAsync(
                    cleanTemp,
                    emptyRecycle,
                    clearLogs,
                    resetPower,
                    triggerOobe);

                if (success)
                {
                    BtnExecutePrep.Content = "✓ RETAIL PREP COMPLETED";
                    BtnExecutePrep.IsEnabled = true;
                }
            }
            finally
            {
                RetailPrepService.Instance.ProgressUpdated -= handler;
            }
        }
    }
}
