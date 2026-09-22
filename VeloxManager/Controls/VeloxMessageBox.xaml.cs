using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace VeloxManager.Controls;

public partial class VeloxMessageBox : Window
{
    public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

    public VeloxMessageBox()
    {
        InitializeComponent();
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.Cancel;
        Close();
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.OK;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.Cancel;
        Close();
    }

    private void BtnYes_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.Yes;
        Close();
    }

    private void BtnNo_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.No;
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Result = BtnCancel.Visibility == Visibility.Visible ? MessageBoxResult.Cancel :
                     BtnNo.Visibility == Visibility.Visible ? MessageBoxResult.No : MessageBoxResult.OK;
            Close();
        }
        else if (e.Key == Key.Enter)
        {
            Result = BtnYes.Visibility == Visibility.Visible ? MessageBoxResult.Yes : MessageBoxResult.OK;
            Close();
        }
    }

    public static MessageBoxResult Show(
        string message,
        string title = "Velox Depot",
        MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.Information,
        Window? owner = null)
    {
        if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return Application.Current.Dispatcher.Invoke(() => ShowInternal(message, title, buttons, icon, owner));
        }

        return ShowInternal(message, title, buttons, icon, owner);
    }

    private static MessageBoxResult ShowInternal(
        string message,
        string title,
        MessageBoxButton buttons,
        MessageBoxImage icon,
        Window? owner)
    {
        var dlg = new VeloxMessageBox();
        dlg.TxtTitle.Text = title;
        dlg.TxtMessage.Text = message;

        // Configure Icon & Color Badge
        switch (icon)
        {
            case MessageBoxImage.Error: // or Hand/Stop
                dlg.TxtIcon.Text = "✕";
                dlg.IconBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DA3633"));
                break;
            case MessageBoxImage.Warning: // or Exclamation
                dlg.TxtIcon.Text = "⚠";
                dlg.IconBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D29922"));
                break;
            case MessageBoxImage.Question:
                dlg.TxtIcon.Text = "?";
                dlg.IconBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8957E5"));
                break;
            case MessageBoxImage.Information: // or Asterisk
            default:
                if (title.Contains("Success", StringComparison.OrdinalIgnoreCase) ||
                    title.Contains("Updated", StringComparison.OrdinalIgnoreCase) ||
                    title.Contains("Certified", StringComparison.OrdinalIgnoreCase) ||
                    title.Contains("Generated", StringComparison.OrdinalIgnoreCase))
                {
                    dlg.TxtIcon.Text = "✓";
                    dlg.IconBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#238636"));
                }
                else
                {
                    dlg.TxtIcon.Text = "ℹ";
                    dlg.IconBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1F6FEB"));
                }
                break;
        }

        // Configure Buttons
        switch (buttons)
        {
            case MessageBoxButton.OKCancel:
                dlg.BtnOk.Visibility = Visibility.Visible;
                dlg.BtnCancel.Visibility = Visibility.Visible;
                dlg.BtnYes.Visibility = Visibility.Collapsed;
                dlg.BtnNo.Visibility = Visibility.Collapsed;
                break;
            case MessageBoxButton.YesNo:
                dlg.BtnOk.Visibility = Visibility.Collapsed;
                dlg.BtnCancel.Visibility = Visibility.Collapsed;
                dlg.BtnYes.Visibility = Visibility.Visible;
                dlg.BtnNo.Visibility = Visibility.Visible;
                break;
            case MessageBoxButton.YesNoCancel:
                dlg.BtnOk.Visibility = Visibility.Collapsed;
                dlg.BtnCancel.Visibility = Visibility.Visible;
                dlg.BtnYes.Visibility = Visibility.Visible;
                dlg.BtnNo.Visibility = Visibility.Visible;
                break;
            case MessageBoxButton.OK:
            default:
                dlg.BtnOk.Visibility = Visibility.Visible;
                dlg.BtnCancel.Visibility = Visibility.Collapsed;
                dlg.BtnYes.Visibility = Visibility.Collapsed;
                dlg.BtnNo.Visibility = Visibility.Collapsed;
                break;
        }

        // Determine owner window safely
        var targetOwner = owner ?? Application.Current?.MainWindow;
        if (targetOwner != null && targetOwner.IsVisible)
        {
            dlg.Owner = targetOwner;
            dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        dlg.ShowDialog();
        return dlg.Result;
    }
}
