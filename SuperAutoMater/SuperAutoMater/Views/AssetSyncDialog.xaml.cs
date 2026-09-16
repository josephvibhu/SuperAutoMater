using System;
using System.IO;
using System.Media;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using QRCoder;
using SuperAutoMater.Wpf.Core;
using SuperAutoMater.Wpf.Services;
using SuperAutoMater.Wpf.ViewModels;

namespace SuperAutoMater.Wpf.Views
{
    public partial class AssetSyncDialog : Window
    {
        public AssetQueueRecord GeneratedRecord { get; private set; }
        private AssetQueueRecord _priorRecord = null;

        public AssetSyncDialog(MainViewModel vm)
        {
            InitializeComponent();

            if (vm != null)
            {
                TxtSerial.Text = !string.IsNullOrWhiteSpace(vm.Serial) && vm.Serial != "Detecting..." ? vm.Serial : "QC-SN-" + DateTime.Now.ToString("MMdd");
                TxtAssetTag.Text = TxtSerial.Text;
                TxtModel.Text = vm.Model;
                TxtCpu.Text = vm.CpuName;
                TxtRamStorage.Text = $"{vm.RamSummary} / {vm.StorageSummary}";
                TxtBatteryHealth.Text = vm.BatteryHealth.ToString();
                TxtStorageHealth.Text = vm.HdsHealth.ToString();

                string gradeClean = vm.Grade?.Replace("GRADE", "")?.Trim() ?? "A+";
                foreach (ComboBoxItem item in CmbGrade.Items)
                {
                    if (string.Equals(item.Content?.ToString(), gradeClean, StringComparison.OrdinalIgnoreCase))
                    {
                        CmbGrade.SelectedItem = item;
                        break;
                    }
                }

                // Check if any tests failed to preselect WIP / defect
                bool hasFailures = false;
                string firstFailure = "";
                foreach (var test in vm.TestPipeline)
                {
                    if (test.Status == "FAILED" || (!test.IsPassed && test.Status != "PENDING" && test.Status != "NOT RUN" && test.IsApplicable))
                    {
                        hasFailures = true;
                        firstFailure = $"{test.Title} issue";
                        break;
                    }
                }

                if (hasFailures)
                {
                    foreach (ComboBoxItem item in CmbStatus.Items)
                    {
                        if (item.Content?.ToString() == "WIP")
                        {
                            CmbStatus.SelectedItem = item;
                            break;
                        }
                    }
                    CmbWorkInProgress.Text = firstFailure;
                }
            }

            TxtInDate.Text = DateTime.Now.ToString("yyyy-MM-dd");

            try
            {
                var tech = TechnicianProfileService.Instance.CurrentProfile;
                TxtTechnician.Text = !string.IsNullOrWhiteSpace(tech.Id) ? tech.Id : (tech.Name ?? "TECH-01");
            }
            catch
            {
                TxtTechnician.Text = "TECH-01";
            }

            Loaded += (s, e) =>
            {
                RefreshQrCode();
                CheckAssetHistoryAsync(TxtSerial.Text, TxtAssetTag.Text);
            };
        }

        private void Field_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshQrCode();
            CheckAssetHistoryAsync(TxtSerial?.Text, TxtAssetTag?.Text);
        }

        private void BtnApplyPriorMetadata_Click(object sender, RoutedEventArgs e)
        {
            if (_priorRecord == null) return;

            if (!string.IsNullOrWhiteSpace(_priorRecord.Supplier))
                TxtSupplier.Text = _priorRecord.Supplier;

            if (!string.IsNullOrWhiteSpace(_priorRecord.Customer))
                TxtCustomer.Text = _priorRecord.Customer;

            if (!string.IsNullOrWhiteSpace(_priorRecord.Shelf_Location))
                TxtShelf.Text = _priorRecord.Shelf_Location;

            if (!string.IsNullOrWhiteSpace(_priorRecord.Model) && (string.IsNullOrWhiteSpace(TxtModel.Text) || TxtModel.Text.Contains("Detecting")))
                TxtModel.Text = _priorRecord.Model;

            MessageBox.Show("Prior provenance metadata (Supplier, Customer, Shelf Bay, Model) applied successfully!", "Historical Metadata Recall", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void CheckAssetHistoryAsync(string serial, string tag)
        {
            string q = !string.IsNullOrWhiteSpace(serial) && serial != "Unidentified" && !serial.StartsWith("QC-SN-")
                ? serial.Trim()
                : (!string.IsNullOrWhiteSpace(tag) && !tag.StartsWith("QC-SN-") ? tag.Trim() : "");

            if (string.IsNullOrWhiteSpace(q))
            {
                if (PnlHistoryComparison != null) PnlHistoryComparison.Visibility = Visibility.Collapsed;
                return;
            }

            try
            {
                AssetQueueRecord prior = null;
                string source = "";

                // 1. Check local SQLite store (system of record)
                var store = new QcRunStore();
                var localAsset = store.FindAssetBySerialOrTag(q);
                if (localAsset != null && !string.IsNullOrEmpty(localAsset.AssetId))
                {
                    prior = new AssetQueueRecord
                    {
                        Tag = localAsset.AssetTag,
                        Serial_Number = localAsset.SerialNumber,
                        Model = localAsset.Model,
                        Storage_Health = localAsset.StorageHealth,
                        Status = localAsset.LatestRunStatus ?? "RTS",
                        Work_In_Progress = localAsset.WorkInProgress,
                        Physical_Grade = localAsset.LatestRunGrade ?? "A+",
                        Remarks = localAsset.Remarks,
                        In_Date = localAsset.InDate,
                        Supplier = localAsset.Supplier,
                        Out_Date = localAsset.OutDate,
                        Customer = localAsset.Customer,
                        Shelf_Location = localAsset.CurrentLocation,
                        Timestamp = localAsset.UpdatedAtUtc.ToString("yyyy-MM-dd HH:mm")
                    };
                    source = "Local SQLite Store";
                }

                // 2. Check local ledger if not found or to supplement battery health
                var audit = OfflineLedgerService.Instance.FindLatestRecord(q);
                if (audit != null)
                {
                    int.TryParse((audit.Battery_Health ?? "").Replace("%", "").Trim(), out int bh);
                    if (prior == null)
                    {
                        prior = new AssetQueueRecord
                        {
                            Tag = audit.Tag,
                            Serial_Number = audit.Serial_Number,
                            Model = audit.Model,
                            Processor = audit.CPU_Model,
                            Memory = audit.RAM_GB,
                            Battery_Health = bh > 0 ? bh : 100,
                            Storage_Health = audit.Storage_Health,
                            Status = audit.Status,
                            Work_In_Progress = audit.Work_In_Progress,
                            Physical_Grade = audit.Physical_Grade,
                            Remarks = audit.Technician_Notes,
                            Technician = audit.Technician,
                            In_Date = audit.In_Date,
                            Supplier = audit.Supplier,
                            Out_Date = audit.Out_Date,
                            Customer = audit.Customer,
                            Timestamp = audit.Timestamp.ToString("yyyy-MM-dd HH:mm")
                        };
                        source = "Local ITAM Ledger";
                    }
                    else if (bh > 0 && prior.Battery_Health == 100)
                    {
                        prior.Battery_Health = bh;
                    }
                }

                // 3. If still not found, check Google Sheets remote query
                if (prior == null)
                {
                    var remote = await OfflineSyncQueue.Instance.QueryRemoteSheetAsync(q);
                    if (remote != null)
                    {
                        prior = remote;
                        source = "Google Sheets Cloud";
                    }
                }

                if (prior != null)
                {
                    _priorRecord = prior;
                    RenderComparison(prior, source);
                }
                else
                {
                    if (PnlHistoryComparison != null) PnlHistoryComparison.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"History check error: {ex.Message}");
            }
        }

        private void RenderComparison(AssetQueueRecord prior, string source)
        {
            if (PnlHistoryComparison == null) return;
            PnlHistoryComparison.Visibility = Visibility.Visible;
            TxtHistorySource.Text = $"{source.ToUpper()} MATCH";

            string dateStr = !string.IsNullOrWhiteSpace(prior.In_Date) ? prior.In_Date : prior.Timestamp;
            string techStr = !string.IsNullOrWhiteSpace(prior.Technician) ? $" by {prior.Technician}" : "";
            TxtHistorySummary.Text = $"Prior record from {dateStr}{techStr} · Model: {prior.Model}";

            // Battery Delta
            int curB = 100;
            int.TryParse(TxtBatteryHealth.Text, out curB);
            int prevB = prior.Battery_Health;
            int bDiff = curB - prevB;
            string bSign = bDiff > 0 ? "+" : "";
            TxtBatteryDelta.Text = prevB > 0 ? $"{prevB}% ➔ {curB}% ({bSign}{bDiff}%)" : $"{curB}% (Prev N/A)";
            TxtBatteryDelta.Foreground = bDiff < -5
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 158, 11))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(56, 189, 248));

            // Storage Delta
            int curS = 100;
            int.TryParse(TxtStorageHealth.Text, out curS);
            int prevS = prior.Storage_Health;
            int sDiff = curS - prevS;
            string sSign = sDiff > 0 ? "+" : "";
            TxtStorageDelta.Text = prevS > 0 ? $"{prevS}% ➔ {curS}% ({sSign}{sDiff}%)" : $"{curS}%";

            // Status / WIP Delta
            string curStatus = (CmbStatus.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "RTS";
            string prevStatus = prior.Status ?? "RTS";
            TxtStatusDelta.Text = $"{prevStatus} ➔ {curStatus}";

            // Grade Delta
            string curGrade = (CmbGrade.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "A+";
            string prevGrade = prior.Physical_Grade ?? "A+";
            TxtGradeDelta.Text = $"{prevGrade} ➔ {curGrade}";

            // Prior Defect Note
            if (!string.IsNullOrWhiteSpace(prior.Work_In_Progress) && prior.Work_In_Progress != "All Okay")
            {
                TxtPriorDefectNote.Visibility = Visibility.Visible;
                if (curStatus == "RTS")
                {
                    TxtPriorDefectNote.Text = $"✓ Prior defect was '{prior.Work_In_Progress}' — Marked RESOLVED in current test!";
                    TxtPriorDefectNote.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 211, 153));
                }
                else
                {
                    TxtPriorDefectNote.Text = $"⚠ Prior defect was '{prior.Work_In_Progress}' — Currently in status '{curStatus}'.";
                    TxtPriorDefectNote.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(251, 191, 36));
                }
            }
            else
            {
                TxtPriorDefectNote.Visibility = Visibility.Collapsed;
            }
        }

        private void RefreshQrCode()
        {
            try
            {
                string tag = TxtAssetTag?.Text ?? "";
                string sn = TxtSerial?.Text ?? "";
                string cpu = TxtCpu?.Text ?? "";
                string mem = TxtRamStorage?.Text ?? "";
                string bh = TxtBatteryHealth?.Text ?? "100";
                string sh = TxtStorageHealth?.Text ?? "100";

                string payload = $"TAG:{tag}|SN:{sn}|CPU:{cpu}|MEM:{mem}|BAT:{bh}|STR:{sh}";
                using var qrGenerator = new QRCodeGenerator();
                using var qrData = qrGenerator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
                using var qrCode = new PngByteQRCode(qrData);
                byte[] qrBytes = qrCode.GetGraphic(6);

                var image = new BitmapImage();
                using (var memStream = new MemoryStream(qrBytes))
                {
                    memStream.Position = 0;
                    image.BeginInit();
                    image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.UriSource = null;
                    image.StreamSource = memStream;
                    image.EndInit();
                }
                image.Freeze();
                ImgQrCode.Source = image;
                TxtQrCaption.Text = $"TAG: {tag} · SN: {sn}";
            }
            catch { }
        }

        private void BtnPrintLabel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var record = BuildCurrentRecord();
                ThermalLabelPrinter.PrintLabel(record);
                MessageBox.Show("Thermal Chassis Label sent to printer!", "Label Printer", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Printing Error: " + ex.Message, "Printer Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void BtnSubmit_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var record = BuildCurrentRecord();
                GeneratedRecord = record;

                // Play affirmative confirmation chime
                try { SystemSounds.Asterisk.Play(); } catch { }

                // 1. Save to local SQLite database (ITAM system of record)
                try
                {
                    var store = new QcRunStore();
                    store.SaveItamRecord(record);
                }
                catch (Exception dbEx)
                {
                    AppLogger.Warn($"Failed to save ITAM record to SQLite: {dbEx.Message}");
                }

                // 2. Enqueue to persistent offline queue
                OfflineSyncQueue.Instance.Enqueue(record);

                // 3. Flush queue to Google Sheets webhook
                int flushed = await OfflineSyncQueue.Instance.FlushQueueAsync(OfflineSyncQueue.GetActiveWebhookUrl());

                string msg = flushed > 0
                    ? $"Asset {record.Serial_Number} successfully synced to Google Sheets!\n\nTag: {record.Tag} [Bold]\nStatus: {record.Status} | Grade: {record.Physical_Grade}\nBattery: {record.Battery_Health} | Storage: {record.Storage_Health}\nTechnician: {record.Technician}"
                    : $"Asset {record.Serial_Number} saved to Local ITAM Ledger & Offline Queue ({OfflineSyncQueue.Instance.PendingCount} items pending).\nWill automatically sync once online.";

                MessageBox.Show(msg, "ITAM Asset Synchronization Success", MessageBoxButton.OK, MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Cloud Sync Failed: " + ex.Message, "Sync Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private AssetQueueRecord BuildCurrentRecord()
        {
            int bHealth = 100;
            int.TryParse(TxtBatteryHealth.Text, out bHealth);

            int sHealth = 100;
            int.TryParse(TxtStorageHealth.Text, out sHealth);

            string grade = (CmbGrade.SelectedItem as ComboBoxItem)?.Content?.ToString()
                           ?? CmbGrade.Text?.Trim()
                           ?? "A+";

            string status = (CmbStatus.SelectedItem as ComboBoxItem)?.Content?.ToString()
                            ?? CmbStatus.Text?.Trim()
                            ?? "RTS";

            string wip = (CmbWorkInProgress.SelectedItem as ComboBoxItem)?.Content?.ToString()
                         ?? CmbWorkInProgress.Text?.Trim()
                         ?? "All Okay";
            if (string.IsNullOrWhiteSpace(wip)) wip = "All Okay";

            string tag = TxtAssetTag.Text.Trim();
            string serial = TxtSerial.Text.Trim();
            if (string.IsNullOrWhiteSpace(tag)) tag = serial;

            return new AssetQueueRecord
            {
                Tag = tag,
                Asset_Tag = tag,
                Serial_Number = serial,
                Model = TxtModel.Text.Trim(),
                Processor = TxtCpu.Text.Trim(),
                Memory = TxtRamStorage.Text.Trim(),
                Battery_Health = Math.Clamp(bHealth, 0, 100),
                Storage_Health = Math.Clamp(sHealth, 0, 100),
                Status = status,
                Work_In_Progress = wip,
                Wip_Issue = wip,
                Physical_Grade = grade,
                Remarks = TxtRemarks.Text.Trim(),
                Shelf_Location = TxtShelf.Text.Trim(),
                Technician = TxtTechnician.Text.Trim(),
                In_Date = TxtInDate.Text.Trim(),
                Supplier = TxtSupplier.Text.Trim(),
                Out_Date = TxtOutDate.Text.Trim(),
                Customer = TxtCustomer.Text.Trim(),
                Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
        }

        private void BtnSheetsSetup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StartupServices.EnsureSheetsSetupFiles();
                Clipboard.SetText(StartupServices.CodeGsContent);
                var res = MessageBox.Show(
                    "The updated Google Apps Script (code.gs) for 15-column ITAM synchronization has been copied to your Windows Clipboard!\n\n" +
                    "To update your Google Sheet:\n" +
                    "1. Open your Google Sheet.\n" +
                    "2. Click Extensions → Apps Script.\n" +
                    "3. Select all existing code (Ctrl+A), delete, and paste (Ctrl+V).\n" +
                    "4. Click Deploy → Manage Deployments → Edit (pencil icon) → Version: New Version → Deploy.\n\n" +
                    "Would you like to open the Sheets_Setup folder with the code.gs file?",
                    "Google Apps Script (code.gs) Copied",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (res == MessageBoxResult.Yes)
                {
                    StartupServices.OpenSheetsSetupFolder();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to copy code.gs: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnChangeWebhook_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string localFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sheets_url.txt");
                if (!System.IO.File.Exists(localFile))
                {
                    System.IO.File.WriteAllText(localFile, OfflineSyncQueue.GetActiveWebhookUrl());
                }

                var res = MessageBox.Show(
                    $"Current Webhook URL:\n{OfflineSyncQueue.GetActiveWebhookUrl()}\n\n" +
                    "To point SuperAutoMater to a NEW Google Sheet:\n" +
                    "1. Click 'Yes' to open 'sheets_url.txt' in Notepad.\n" +
                    "2. Paste your new Web App URL on line 1.\n" +
                    "3. Save (Ctrl+S) and close Notepad.\n\n" +
                    "The app will immediately use the new Google Sheet URL for all syncs.\n\n" +
                    "Open 'sheets_url.txt' now?",
                    "Configure Google Sheets Webhook URL",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (res == MessageBoxResult.Yes)
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe", $"\"{localFile}\"") { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to open sheets_url.txt: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
