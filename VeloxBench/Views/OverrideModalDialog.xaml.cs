using System;
using System.Windows;
using System.Windows.Controls;
using SuperAutoMater.Wpf.Core;

namespace SuperAutoMater.Wpf.Views
{
    public partial class OverrideModalDialog : Window
    {
        private readonly string _testKey;
        private readonly bool _requiresSupervisor;

        public string Reason { get; private set; } = "";
        public string Actor { get; private set; } = "";
        public string Approver { get; private set; } = "";

        public OverrideModalDialog(string testKey, string testTitle, string currentTechnician, bool requiresSupervisor)
        {
            InitializeComponent();
            _testKey = testKey;
            _requiresSupervisor = requiresSupervisor;

            TxtTestKeyBadge.Text = (testKey ?? "TEST").ToUpperInvariant();
            TxtActor.Text = string.IsNullOrWhiteSpace(currentTechnician) ? "TECH-01" : currentTechnician;
            TxtReasonDetail.Text = "Customer waiver / Known cosmetic exemption";

            if (_requiresSupervisor)
            {
                PanelSupervisor.Visibility = Visibility.Visible;
            }
        }

        private void CmbReasonPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TxtReasonDetail == null) return;
            if (CmbReasonPreset.SelectedItem is ComboBoxItem item)
            {
                string text = item.Content?.ToString() ?? "";
                if (!text.StartsWith("Custom", StringComparison.OrdinalIgnoreCase))
                {
                    TxtReasonDetail.Text = text;
                }
                else
                {
                    TxtReasonDetail.Text = "";
                    TxtReasonDetail.Focus();
                }
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnSubmit_Click(object sender, RoutedEventArgs e)
        {
            string actor = TxtActor.Text?.Trim() ?? "";
            string reason = TxtReasonDetail.Text?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(actor))
            {
                MessageBox.Show("Technician ID / Name is required.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtActor.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                MessageBox.Show("A specific reason or justification is required to override this diagnostic test.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtReasonDetail.Focus();
                return;
            }

            string approver = "";
            if (_requiresSupervisor)
            {
                approver = TxtSupervisorName.Text?.Trim() ?? "";
                string pin = TxtSupervisorPin.Password?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(approver))
                {
                    MessageBox.Show("Supervisor name or Lead ID is required by policy.", "Audit Gate", MessageBoxButton.OK, MessageBoxImage.Warning);
                    TxtSupervisorName.Focus();
                    return;
                }
                if (string.IsNullOrWhiteSpace(pin))
                {
                    MessageBox.Show("Supervisor authorization PIN is required.", "Audit Gate", MessageBoxButton.OK, MessageBoxImage.Warning);
                    TxtSupervisorPin.Focus();
                    return;
                }
            }

            Actor = actor;
            Reason = reason;
            Approver = approver;

            DialogResult = true;
            Close();
        }
    }
}
