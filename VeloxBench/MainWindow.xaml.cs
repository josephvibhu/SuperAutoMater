using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using VeloxBench.ViewModels;

namespace VeloxBench;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = MainViewModel.Instance;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var vm = DataContext as MainViewModel;
        if (vm == null) return;

        // F5 -> Run Full 8-Test Express QC
        if (e.Key == Key.F5)
        {
            e.Handled = true;
            if (vm.RunQcCommand.CanExecute(null))
                vm.RunQcCommand.Execute(null);
            return;
        }

        // On-Demand Single Subsystem Diagnostic Hotkeys
        string? singleKey = e.Key switch
        {
            Key.F1 => "Camera",
            Key.F2 => "Mic",
            Key.F3 => "Speaker",
            Key.F4 => "Storage",
            Key.F6 => "CpuRam",
            Key.F7 => "Battery",
            Key.F8 => "Display",
            Key.F9 => "Network",
            _ => null
        };

        if (singleKey != null)
        {
            e.Handled = true;
            if (vm.RunSingleSubsystemCommand.CanExecute(singleKey))
                vm.RunSingleSubsystemCommand.Execute(singleKey);
            return;
        }

        // Ctrl+P -> Print Certificate
        if (e.Key == Key.P && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            e.Handled = true;
            if (vm.GeneratePdfCommand.CanExecute(null))
                vm.GeneratePdfCommand.Execute(null);
            return;
        }

        // Enter -> Pass QC & Push Cloud (when not typing in a multi-line text input)
        if (e.Key == Key.Enter && !(e.OriginalSource is TextBox tb && tb.AcceptsReturn))
        {
            e.Handled = true;
            if (vm.PassQcAndPushCloudCommand.CanExecute(null))
                vm.PassQcAndPushCloudCommand.Execute(null);
            return;
        }
    }

    private void SubsystemCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is Services.ExpressSubsystemResult sub)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.SelectedSubsystem = sub;
            }
        }
    }
}