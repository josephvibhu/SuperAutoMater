using System.Windows;
using System.Windows.Controls;

namespace VeloxDeploy
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void ConsoleLogBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ConsoleLogBox.ScrollToEnd();
        }
    }
}
