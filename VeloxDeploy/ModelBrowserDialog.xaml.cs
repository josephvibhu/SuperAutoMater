using System.Windows;
using System.Windows.Input;
using VeloxDeploy.Models;
using VeloxDeploy.ViewModels;

namespace VeloxDeploy
{
    public partial class ModelBrowserDialog : Window
    {
        public ModelBrowserViewModel ViewModel { get; }

        public string? SelectedModelName => ViewModel.SelectedItem?.ModelName;

        public ModelBrowserDialog(string? selectedModel = null, HardwareProfile? hostProfile = null)
        {
            InitializeComponent();
            ViewModel = new ModelBrowserViewModel(selectedModel, hostProfile);
            DataContext = ViewModel;

            ViewModel.RequestClose += success =>
            {
                DialogResult = success;
                Close();
            };

            Loaded += (_, _) =>
            {
                SearchBox.Focus();
            };
        }

        private void ListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel.SelectedItem != null && ViewModel.SelectCommand.CanExecute(null))
            {
                ViewModel.SelectCommand.Execute(null);
            }
        }
    }
}
