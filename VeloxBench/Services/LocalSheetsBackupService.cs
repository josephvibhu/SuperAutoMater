using System;
using System.IO;
using System.Text;
using SuperAutoMater;
using SuperAutoMater.Core;
using SuperAutoMater.Wpf.Core;

namespace VeloxBench.Services
{
    /// <summary>
    /// Dual Sheets Backup Service:
    /// Maintains an automatic, persistent Excel/CSV sheets file backup on the local disk / USB drive
    /// in the exact 16-column ITAM format matching Google Sheets:
    /// Tag, Serial Number, Model, Processor, Memory [ram/storage], Battery health, Storage Health, Status,
    /// Work in Progress, Physical Grade, Remarks, Technician Name or ID, In Date, Supplier, Out Date, Customer.
    /// </summary>
    public static class LocalSheetsBackupService
    {
        private static readonly object _lock = new object();

        private const string CsvHeader = "Tag,Serial Number,Model,Processor,Memory [ram/storage],GPU,Battery health,Storage Health,Status,Work in Progress,Physical Grade,Remarks,Technician Name or ID,In Date,Supplier,Out Date,Customer";

        public static string GetPrimaryCsvPath()
        {
            try
            {
                // Preference 1: Right beside the running executable (e.g. on technician's USB drive)
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string usbCsv = Path.Combine(baseDir, "Velox_QC_Inventory_Backup.csv");
                return usbCsv;
            }
            catch
            {
                // Fallback: LocalApplicationData
                string localDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VeloxBench", "Reports");
                Directory.CreateDirectory(localDir);
                return Path.Combine(localDir, "Velox_QC_Inventory_Backup.csv");
            }
        }

        public static string GetSecondaryCsvPath()
        {
            try
            {
                string localDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VeloxBench", "Reports");
                if (!Directory.Exists(localDir))
                    Directory.CreateDirectory(localDir);
                return Path.Combine(localDir, "Velox_QC_Inventory_Backup.csv");
            }
            catch
            {
                return "";
            }
        }

        public static void AppendRecord(AssetQueueRecord rec)
        {
            if (rec == null) return;

            string line = FormatCsvLine(rec);
            lock (_lock)
            {
                // Write to primary (beside exe / USB)
                SafeAppend(GetPrimaryCsvPath(), line);

                // Write secondary mirror (LocalAppData)
                string sec = GetSecondaryCsvPath();
                if (!string.IsNullOrWhiteSpace(sec) && !string.Equals(sec, GetPrimaryCsvPath(), StringComparison.OrdinalIgnoreCase))
                {
                    SafeAppend(sec, line);
                }
            }
        }

        private static void SafeAppend(string path, string line)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                bool fileExists = File.Exists(path);
                using var sw = new StreamWriter(path, append: true, encoding: Encoding.UTF8);
                if (!fileExists || new FileInfo(path).Length == 0)
                {
                    sw.WriteLine(CsvHeader);
                }
                sw.WriteLine(line);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to append record to CSV '{path}': {ex.Message}");
            }
        }

        private static string FormatCsvLine(AssetQueueRecord r)
        {
            string tag = EscapeCsv(string.IsNullOrWhiteSpace(r.Tag) ? r.Asset_Tag : r.Tag);
            string sn = EscapeCsv(r.Serial_Number);
            string model = EscapeCsv(r.Model);
            string cpu = EscapeCsv(r.Processor);
            string mem = EscapeCsv(r.Memory);
            string gpu = EscapeCsv(r.Gpu);
            string batt = EscapeCsv(r.Battery_Health > 0 ? r.Battery_Health.ToString() : "100");
            string storage = EscapeCsv(r.Storage_Health > 0 ? r.Storage_Health.ToString() : "100");
            string status = EscapeCsv(string.IsNullOrWhiteSpace(r.Status) ? "RTS" : r.Status);
            string wip = EscapeCsv(string.IsNullOrWhiteSpace(r.Work_In_Progress) ? "All Okay" : r.Work_In_Progress);
            string grade = EscapeCsv(string.IsNullOrWhiteSpace(r.Physical_Grade) ? "A+" : r.Physical_Grade);
            string remarks = EscapeCsv(r.Remarks);
            string tech = EscapeCsv(string.IsNullOrWhiteSpace(r.Technician) ? "TECH-01" : r.Technician);
            string inDate = EscapeCsv(string.IsNullOrWhiteSpace(r.In_Date) ? DateTime.Now.ToString("yyyy-MM-dd") : r.In_Date);
            string supplier = EscapeCsv(r.Supplier);
            string outDate = EscapeCsv(r.Out_Date);
            string customer = EscapeCsv(r.Customer);

            return $"{tag},{sn},{model},{cpu},{mem},{gpu},{batt},{storage},{status},{wip},{grade},{remarks},{tech},{inDate},{supplier},{outDate},{customer}";
        }

        private static string EscapeCsv(string val)
        {
            if (string.IsNullOrEmpty(val)) return "";
            if (val.Contains(",") || val.Contains("\"") || val.Contains("\n") || val.Contains("\r"))
            {
                return $"\"{val.Replace("\"", "\"\"")}\"";
            }
            return val;
        }
    }
}
