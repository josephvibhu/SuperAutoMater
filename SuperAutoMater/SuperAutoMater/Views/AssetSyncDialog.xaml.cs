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

            Loaded += (s, e) => RefreshQrCode();
        }

        private void Field_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshQrCode();
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
                int flushed = await OfflineSyncQueue.Instance.FlushQueueAsync(OfflineSyncQueue.DefaultSheetsUrl);

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

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
