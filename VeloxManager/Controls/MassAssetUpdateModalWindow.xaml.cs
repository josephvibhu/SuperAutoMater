using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Velox.Shared.Dtos;
using Velox.Shared.Models;
using VeloxManager.Services;
using VeloxManager.ViewModels;

namespace VeloxManager.Controls
{
    public partial class MassAssetUpdateModalWindow : Window
    {
        private readonly List<AssetItemViewModel> _allAssets;
        private readonly List<AssetItemViewModel> _matchedAssets = new();
        private CancellationTokenSource? _updateCts;

        public int UpdatedCount { get; private set; }

        public MassAssetUpdateModalWindow(IEnumerable<AssetItemViewModel> allAssets, string? initialInputs = null)
        {
            InitializeComponent();
            _allAssets = allAssets.ToList();

            ChkUpdateStatus.IsChecked = true;
            if (CmbTargetStatus != null) CmbTargetStatus.IsEnabled = true;
            if (TxtTargetOutDate != null) TxtTargetOutDate.Text = DateTime.Now.ToString("yyyy-MM-dd");

            if (!string.IsNullOrWhiteSpace(initialInputs))
            {
                TxtAssetInputs.Text = initialInputs;
                PerformMatch();
            }
        }

        private void TxtAssetInputs_TextChanged(object sender, TextChangedEventArgs e)
        {
            PerformMatch();
        }

        private void BtnMatchNow_Click(object sender, RoutedEventArgs e)
        {
            PerformMatch();
        }

        private void BtnClearList_Click(object sender, RoutedEventArgs e)
        {
            TxtAssetInputs.Text = "";
            _matchedAssets.Clear();
            GridMatchedAssets.ItemsSource = null;
            TxtMatchedCount.Text = "0 Matched";
            TxtUnmatchedCount.Text = "";
            TxtApplyBtn.Text = "Execute Mass Update";
        }

        private void PerformMatch()
        {
            if (TxtAssetInputs == null || GridMatchedAssets == null || TxtMatchedCount == null || TxtUnmatchedCount == null || TxtApplyBtn == null)
                return;

            var raw = TxtAssetInputs.Text;
            if (string.IsNullOrWhiteSpace(raw))
            {
                _matchedAssets.Clear();
                GridMatchedAssets.ItemsSource = null;
                TxtMatchedCount.Text = "0 Matched";
                TxtUnmatchedCount.Text = "";
                TxtApplyBtn.Text = "Execute Mass Update";
                return;
            }

            char[] sep = { '\r', '\n', ',', ';', '\t', ' ' };
            var tokens = raw.Split(sep, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();

            var matched = new List<AssetItemViewModel>();
            var unmatched = new List<string>();

            foreach (var token in tokens)
            {
                var found = _allAssets.FirstOrDefault(a =>
                    string.Equals(a.AssetTag, token, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a.SerialNumber, token, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a.Id, token, StringComparison.OrdinalIgnoreCase));

                if (found != null)
                {
                    if (!matched.Contains(found))
                    {
                        matched.Add(found);
                    }
                }
                else
                {
                    unmatched.Add(token);
                }
            }

            _matchedAssets.Clear();
            _matchedAssets.AddRange(matched);
            GridMatchedAssets.ItemsSource = null;
            GridMatchedAssets.ItemsSource = _matchedAssets;

            TxtMatchedCount.Text = $"{_matchedAssets.Count} Matched";
            if (unmatched.Count > 0)
            {
                TxtUnmatchedCount.Text = $"{unmatched.Count} Not Found: {string.Join(", ", unmatched.Take(3))}{(unmatched.Count > 3 ? "..." : "")}";
            }
            else
            {
                TxtUnmatchedCount.Text = "";
            }

            TxtApplyBtn.Text = _matchedAssets.Count > 0 ? $"Execute Mass Update ({_matchedAssets.Count} Assets)" : "Execute Mass Update";
        }

        // ── Presets ───────────────────────────────────────────────────────────
        private void BtnPresetSold_Click(object sender, RoutedEventArgs e)
        {
            ChkUpdateStatus.IsChecked = true;
            SetComboBoxItem(CmbTargetStatus, "Sold");

            ChkUpdateOutDate.IsChecked = true;
            TxtTargetOutDate.Text = DateTime.Now.ToString("yyyy-MM-dd");

            ChkUpdateCustomer.IsChecked = true;
            TxtTargetCustomer.Focus();
        }

        private void BtnPresetRts_Click(object sender, RoutedEventArgs e)
        {
            ChkUpdateStatus.IsChecked = true;
            SetComboBoxItem(CmbTargetStatus, "ReadyForSale");
        }

        private void BtnPresetWip_Click(object sender, RoutedEventArgs e)
        {
            ChkUpdateStatus.IsChecked = true;
            SetComboBoxItem(CmbTargetStatus, "ActiveTesting");
        }

        private static void SetComboBoxItem(ComboBox combo, string content)
        {
            foreach (var item in combo.Items)
            {
                if (item is ComboBoxItem cbi && string.Equals(cbi.Content?.ToString(), content, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedItem = cbi;
                    return;
                }
            }
        }

        // ── Checkbox state helpers ────────────────────────────────────────────
        private void ChkUpdateStatus_Changed(object sender, RoutedEventArgs e)
        {
            if (CmbTargetStatus != null && ChkUpdateStatus != null)
                CmbTargetStatus.IsEnabled = ChkUpdateStatus.IsChecked == true;
        }
        private void ChkUpdateLocation_Changed(object sender, RoutedEventArgs e)
        {
            if (CmbTargetLocation != null && ChkUpdateLocation != null)
                CmbTargetLocation.IsEnabled = ChkUpdateLocation.IsChecked == true;
        }
        private void ChkUpdateGrade_Changed(object sender, RoutedEventArgs e)
        {
            if (CmbTargetGrade != null && ChkUpdateGrade != null)
                CmbTargetGrade.IsEnabled = ChkUpdateGrade.IsChecked == true;
        }
        private void ChkUpdateCustomer_Changed(object sender, RoutedEventArgs e)
        {
            if (TxtTargetCustomer != null && ChkUpdateCustomer != null)
                TxtTargetCustomer.IsEnabled = ChkUpdateCustomer.IsChecked == true;
        }
        private void ChkUpdateOutDate_Changed(object sender, RoutedEventArgs e)
        {
            if (TxtTargetOutDate != null && ChkUpdateOutDate != null)
                TxtTargetOutDate.IsEnabled = ChkUpdateOutDate.IsChecked == true;
        }
        private void ChkUpdateNotes_Changed(object sender, RoutedEventArgs e)
        {
            if (TxtTargetNotes != null && ChkUpdateNotes != null)
                TxtTargetNotes.IsEnabled = ChkUpdateNotes.IsChecked == true;
        }

        // ── Execute Update ───────────────────────────────────────────────────
        private async void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            if (_matchedAssets.Count == 0)
            {
                VeloxMessageBox.Show("No assets matched. Please enter valid asset numbers or tags first.", "No Target Assets", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool doStatus = ChkUpdateStatus.IsChecked == true;
            bool doLoc = ChkUpdateLocation.IsChecked == true;
            bool doGrade = ChkUpdateGrade.IsChecked == true;
            bool doCust = ChkUpdateCustomer.IsChecked == true;
            bool doOutDate = ChkUpdateOutDate.IsChecked == true;
            bool doNotes = ChkUpdateNotes.IsChecked == true;

            if (!doStatus && !doLoc && !doGrade && !doCust && !doOutDate && !doNotes)
            {
                VeloxMessageBox.Show("Please select at least one datapoint to update (Status, Location, Grade, Customer, Out Date, or Notes).", "No Fields Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string targetStatusStr = (CmbTargetStatus.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Sold";
            string targetLoc = CmbTargetLocation.Text?.Trim() ?? "";
            string targetGradeStr = (CmbTargetGrade.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "A";
            string targetCust = TxtTargetCustomer.Text?.Trim() ?? "";
            string targetOutDate = TxtTargetOutDate.Text?.Trim() ?? "";
            string appendNotes = TxtTargetNotes.Text?.Trim() ?? "";

            var confirm = VeloxMessageBox.Show(
                $"Apply mass update to {_matchedAssets.Count} assets in the database?\n\n" +
                (doStatus ? $"• Status -> {targetStatusStr}\n" : "") +
                (doLoc ? $"• Location -> {targetLoc}\n" : "") +
                (doGrade ? $"• Grade -> {targetGradeStr}\n" : "") +
                (doCust ? $"• Customer -> {targetCust}\n" : "") +
                (doOutDate ? $"• Out Date -> {targetOutDate}\n" : "") +
                (doNotes ? $"• Append Notes -> \"{appendNotes}\"\n" : ""),
                "Confirm Mass Update", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            // UI state: Updating
            BtnApply.IsEnabled = false;
            BtnCancel.Content = "Cancel";
            PanelProgress.Visibility = Visibility.Visible;
            PbMassProgress.Value = 0;
            TxtProgressPercent.Text = "0%";
            TxtProgressStatus.Text = $"Starting update of {_matchedAssets.Count} assets...";

            _updateCts = new CancellationTokenSource();
            var token = _updateCts.Token;

            int total = _matchedAssets.Count;
            int success = 0;
            int failed = 0;

            await Task.Run(async () =>
            {
                for (int i = 0; i < total; i++)
                {
                    if (token.IsCancellationRequested) break;

                    var asset = _matchedAssets[i];
                    Dispatcher.Invoke(() =>
                    {
                        int pct = (int)((double)(i + 1) / total * 100.0);
                        PbMassProgress.Value = pct;
                        TxtProgressPercent.Text = $"{pct}%";
                        TxtProgressStatus.Text = $"Updating {asset.AssetTag} ({i + 1}/{total})...";
                    });

                    var patch = new PatchAssetRequest
                    {
                        Actor = ManagerConfigService.Current.ManagerName
                    };

                    if (doStatus && Enum.TryParse<LifecycleQueue>(targetStatusStr, true, out var lq))
                    {
                        patch.LifecycleQueue = lq;
                    }

                    if (doLoc && !string.IsNullOrWhiteSpace(targetLoc))
                    {
                        patch.CurrentLocation = targetLoc;
                    }

                    if (doGrade && Enum.TryParse<AssetGrade>(targetGradeStr, true, out var ag))
                    {
                        patch.Grade = ag;
                        patch.CosmeticGrade = targetGradeStr;
                    }

                    if (doCust && !string.IsNullOrWhiteSpace(targetCust))
                    {
                        patch.Customer = targetCust;
                    }

                    if (doOutDate && !string.IsNullOrWhiteSpace(targetOutDate))
                    {
                        patch.OutDate = targetOutDate;
                    }

                    if (doNotes && !string.IsNullOrWhiteSpace(appendNotes))
                    {
                        patch.Notes = string.IsNullOrWhiteSpace(asset.Notes) ? appendNotes : $"{asset.Notes} | {appendNotes}";
                    }

                    var ok = await ManagerApiClient.Instance.PatchAssetAsync(asset.Id, patch);
                    if (ok)
                    {
                        // Update in-memory view model immediately
                        Dispatcher.Invoke(() =>
                        {
                            if (patch.LifecycleQueue.HasValue) asset.Status = patch.LifecycleQueue.Value;
                            if (patch.CurrentLocation != null) asset.CurrentLocation = patch.CurrentLocation;
                            if (patch.Grade.HasValue) asset.Grade = patch.Grade.Value;
                            if (patch.Customer != null) asset.Customer = patch.Customer;
                            if (patch.OutDate != null) asset.OutDate = patch.OutDate;
                            if (patch.Notes != null) asset.Notes = patch.Notes;
                        });
                        success++;
                    }
                    else
                    {
                        failed++;
                    }
                }
            }, token);

            UpdatedCount = success;
            PanelProgress.Visibility = Visibility.Collapsed;
            BtnApply.IsEnabled = true;
            BtnCancel.Content = "Close";

            if (token.IsCancellationRequested)
            {
                VeloxMessageBox.Show($"Mass update stopped. {success} assets updated.", "Update Cancelled", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                VeloxMessageBox.Show($"Mass update completed successfully!\n\n• Updated: {success} assets\n• Failed: {failed}", "Mass Update Success", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (_updateCts != null && !_updateCts.IsCancellationRequested)
            {
                _updateCts.Cancel();
            }
            Close();
        }
    }
}
