using System;
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
using VeloxManager.ViewModels;

namespace VeloxManager;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = ManagerViewModel.Instance;
    }

    private void OnDrawerBackdropMouseDown(object sender, MouseButtonEventArgs e)
    {
        ManagerViewModel.Instance.IsQuickIntakeOpen = false;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (e.Key == Key.K && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            GlobalSearchTextBox?.Focus();
            GlobalSearchTextBox?.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (ManagerViewModel.Instance.IsQuickIntakeOpen)
            {
                ManagerViewModel.Instance.IsQuickIntakeOpen = false;
                e.Handled = true;
            }
            else if (ManagerViewModel.Instance.HasBulkSelection)
            {
                ManagerViewModel.Instance.ClearSelection();
                e.Handled = true;
            }
        }
    }
}
