using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using VeloxManager.Services;

namespace VeloxManager.Controls
{
    public partial class AssetImportModalWindow : Window
    {
        private ParsedAssetBatch? _currentBatch;
        private CancellationTokenSource? _uploadCts;

        public int ImportedCount { get; private set; }

        public AssetImportModalWindow()
        {
            InitializeComponent();
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select Excel or CSV Inventory Manifest",
                Filter = "Supported Files (*.xlsx;*.csv;*.tsv)|*.xlsx;*.csv;*.tsv|Excel Files (*.xlsx)|*.xlsx|CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dlg.ShowDialog() == true)
            {
                LoadFile(dlg.FileName);
            }
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    LoadFile(files[0]);
                }
            }
        }

        private void LoadFile(string path)
        {
            try
            {
                TxtFilePath.Text = path;
                TxtFooterHint.Text = "Parsing file...";

                _currentBatch = ExcelCsvAssetParser.ParseFile(path);

                GridPreview.ItemsSource = _currentBatch.Rows;
                PanelEmptyPrompt.Visibility = _currentBatch.Rows.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
                PanelMetrics.Visibility = Visibility.Visible;

                TxtTotalRows.Text = _currentBatch.TotalRows.ToString();
                TxtValidRows.Text = _currentBatch.ValidCount.ToString();
                TxtInvalidRows.Text = _currentBatch.InvalidCount.ToString();
                TxtDetectedCols.Text = string.Join(", ", _currentBatch.HeadersFound.Take(8)) + (_currentBatch.HeadersFound.Count > 8 ? "..." : "");

                if (_currentBatch.ValidCount > 0)
                {
                    BtnUpload.IsEnabled = true;
                    TxtUploadBtn.Text = $"Upload {_currentBatch.ValidCount} Assets to DB";
                    TxtFooterHint.Text = $"Successfully parsed {_currentBatch.ValidCount} valid assets. Ready to upload to database.";
                }
                else
                {
                    BtnUpload.IsEnabled = false;
                    TxtUploadBtn.Text = "Upload to Database";
                    TxtFooterHint.Text = "No valid assets found (each asset needs at least a Serial Number or Asset Tag).";
                }
            }
            catch (Exception ex)
            {
                PanelMetrics.Visibility = Visibility.Collapsed;
                PanelEmptyPrompt.Visibility = Visibility.Visible;
                GridPreview.ItemsSource = null;
                BtnUpload.IsEnabled = false;
                TxtFooterHint.Text = "Error reading file.";
                MessageBox.Show($"Failed to parse Excel/CSV manifest:\n\n{ex.Message}", "Manifest Parsing Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnUpload_Click(object sender, RoutedEventArgs e)
        {
            if (_currentBatch == null || _currentBatch.ValidCount == 0) return;

            var validRows = _currentBatch.Rows.Where(r => r.IsValid).ToList();
            if (validRows.Count == 0) return;

            string defCat = (CmbDefaultCategory.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Laptop";
            string defLoc = CmbDefaultLocation.Text?.Trim() ?? "IntakeStaging";
            if (string.IsNullOrWhiteSpace(defLoc)) defLoc = "IntakeStaging";
            string defQueue = (CmbDefaultQueue.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "IntakeStaging";

            // UI state: uploading
            BtnUpload.IsEnabled = false;
            BtnBrowse.IsEnabled = false;
            BtnCancel.Content = "Cancel Upload";
            PanelProgress.Visibility = Visibility.Visible;
            PbUpload.Value = 0;
            TxtProgressPercent.Text = "0%";
            TxtProgressStatus.Text = $"Starting upload of {validRows.Count} assets...";

            _uploadCts = new CancellationTokenSource();
            var token = _uploadCts.Token;

            var progress = new Progress<(int Current, int Total, string Status)>(p =>
            {
                int pct = (int)((double)p.Current / p.Total * 100.0);
                PbUpload.Value = pct;
                TxtProgressPercent.Text = $"{pct}%";
                TxtProgressStatus.Text = p.Status;
            });

            try
            {
                var result = await Task.Run(async () =>
                {
                    return await ManagerApiClient.Instance.ImportParsedAssetsAsync(
                        validRows,
                        defCat,
                        defLoc,
                        defQueue,
                        progress,
                        token);
                }, token);

                ImportedCount = result.SuccessCount;

                if (token.IsCancellationRequested)
                {
                    TxtProgressStatus.Text = "Upload cancelled by user.";
                    MessageBox.Show($"Upload cancelled. Ingested {result.SuccessCount} assets before stopping.", "Upload Interrupted", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    string summary = $"✓ Successfully imported {result.SuccessCount} assets into the database!";
                    if (result.FailCount > 0)
                    {
                        summary += $"\n\n⚠ {result.FailCount} rows had errors and were skipped.";
                    }

                    MessageBox.Show(summary, "Excel / CSV Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    DialogResult = true;
                    Close();
                }
            }
            catch (Exception ex)
            {
                TxtProgressStatus.Text = "Upload error: " + ex.Message;
                MessageBox.Show($"Database ingestion error:\n\n{ex.Message}", "Upload Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnUpload.IsEnabled = true;
                BtnBrowse.IsEnabled = true;
                BtnCancel.Content = "Close";
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (_uploadCts != null && !_uploadCts.IsCancellationRequested)
            {
                _uploadCts.Cancel();
            }
            else
            {
                DialogResult = ImportedCount > 0;
                Close();
            }
        }
    }
}
