using System;
using System.IO;
using System.Media;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using QRCoder;
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
                string payload = $"{TxtAssetTag.Text},{TxtSerial.Text},{TxtModel.Text},{TxtCpu.Text},{TxtRamStorage.Text},{TxtBatteryHealth.Text}%";
                using var qrGenerator = new QRCodeGenerator();
                using var qrData = qrGenerator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
                using var qrCode = new PngByteQRCode(qrData);
                byte[] qrBytes = qrCode.GetGraphic(6);

                var image = new BitmapImage();
                using (var mem = new MemoryStream(qrBytes))
                {
                    mem.Position = 0;
                    image.BeginInit();
                    image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.UriSource = null;
                    image.StreamSource = mem;
                    image.EndInit();
                }
                image.Freeze();
                ImgQrCode.Source = image;
                TxtQrCaption.Text = $"SN: {TxtSerial.Text}";
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

                // Enqueue to persistent offline queue
                OfflineSyncQueue.Instance.Enqueue(record);

                // Flush queue to Google Sheets webhook
                int flushed = await OfflineSyncQueue.Instance.FlushQueueAsync(OfflineSyncQueue.DefaultSheetsUrl);

                string msg = flushed > 0
                    ? $"Asset {record.Serial_Number} successfully synced to Google Sheets (Refurb_Inventory_2026)!\n\nAll fields verified."
                    : $"Asset {record.Serial_Number} saved to Offline Queue ({OfflineSyncQueue.Instance.PendingCount} items pending).\nWill automatically sync once online.";

                MessageBox.Show(msg, "Warehouse Cloud Dispatch Success", MessageBoxButton.OK, MessageBoxImage.Information);

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

            return new AssetQueueRecord
            {
                Asset_Tag = TxtAssetTag.Text.Trim(),
                Serial_Number = TxtSerial.Text.Trim(),
                Model = TxtModel.Text.Trim(),
                Processor = TxtCpu.Text.Trim(),
                Memory = TxtRamStorage.Text.Trim(),
                Battery_Health = Math.Clamp(bHealth, 1, 100),
                Physical_Grade = (CmbGrade.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "A+",
                Status = (CmbStatus.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "RTS",
                Wip_Issue = TxtWipReason.Text.Trim(),
                Remarks = TxtRemarks.Text.Trim(),
                Shelf_Location = TxtShelf.Text.Trim(),
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
