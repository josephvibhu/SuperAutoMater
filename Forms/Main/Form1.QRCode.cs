using System;
using System.Drawing;
using System.Windows.Forms;

namespace SuperAutoMater
{
    public partial class Form1
    {
        public void ShowQRCode()
        {
            try
            {
                using (var form = new AssetCsvExportForm(lastModel, lastSerial, lastCpu, lastRam, lastStorageSummary, lastBatteryHealth))
                {
                    form.ShowDialog(this);
                }
            }
            catch (Exception ex)
            {
                DarkMessageBox.Show("QR Payload Error: " + ex.Message, "Error");
            }
        }
    }
}