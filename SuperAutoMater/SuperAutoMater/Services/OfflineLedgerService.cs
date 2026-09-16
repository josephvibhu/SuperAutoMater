using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SuperAutoMater.Wpf.Services
{
    public class QcAuditRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Tag { get; set; } = "";
        public string Serial_Number { get; set; } = "";
        public string Model { get; set; } = "";
        public string Physical_Grade { get; set; } = "A+";
        public string Status { get; set; } = "RTS";
        public string Work_In_Progress { get; set; } = "All Okay";
        public string Technician_Notes { get; set; } = "";
        public string CPU_Model { get; set; } = "";
        public string RAM_GB { get; set; } = "";
        public string Storage_Details { get; set; } = "";
        public string Battery_Health { get; set; } = "100";
        public int Storage_Health { get; set; } = 100;
        public string Technician { get; set; } = "";
        public string In_Date { get; set; } = "";
        public string Supplier { get; set; } = "";
        public string Out_Date { get; set; } = "";
        public string Customer { get; set; } = "";
        public string Battery_Capacity { get; set; } = "";
        public string GPU_Model { get; set; } = "";
        public string Passed_Tests { get; set; } = "";
        public bool IsSyncedToCloud { get; set; } = false;
        public DateTime? SyncedAt { get; set; }
    }

    public class OfflineLedgerService
    {
        private static readonly Lazy<OfflineLedgerService> _instance =
            new Lazy<OfflineLedgerService>(() => new OfflineLedgerService());
        public static OfflineLedgerService Instance => _instance.Value;

        private readonly string _storageDir;
        private readonly string _ledgerFilePath;
        private readonly object _fileLock = new object();
        private readonly Timer _syncTimer;
        private volatile bool _isSyncing = false;

        public event Action<int> PendingCountChanged;
        public int PendingCount { get; private set; } = 0;

        private OfflineLedgerService()
        {
            _storageDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SuperAutoMater");
            _ledgerFilePath = Path.Combine(_storageDir, "superautomater_ledger.jsonl");

            try
            {
                if (!Directory.Exists(_storageDir))
                    Directory.CreateDirectory(_storageDir);

                RefreshPendingCount();
            }
            catch { }

            // Periodically attempt background synchronization every 30 seconds
            _syncTimer = new Timer(async _ => await AutoSyncTimerCallback(), null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30));
        }

        public void SaveRecord(QcAuditRecord record)
        {
            if (record == null) return;
            lock (_fileLock)
            {
                try
                {
                    string json = JsonSerializer.Serialize(record);
                    File.AppendAllLines(_ledgerFilePath, new[] { json }, Encoding.UTF8);
                }
                catch { }
            }
            RefreshPendingCount();
        }

        public List<QcAuditRecord> GetAllRecords()
        {
            var list = new List<QcAuditRecord>();
            lock (_fileLock)
            {
                if (!File.Exists(_ledgerFilePath)) return list;
                try
                {
                    var lines = File.ReadAllLines(_ledgerFilePath, Encoding.UTF8);
                    foreach (var line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        try
                        {
                            var rec = JsonSerializer.Deserialize<QcAuditRecord>(line);
                            if (rec != null) list.Add(rec);
                        }
                        catch { }
                    }
                }
                catch { }
            }
            return list;
        }

        public QcAuditRecord FindLatestRecord(string serialOrTag)
        {
            if (string.IsNullOrWhiteSpace(serialOrTag)) return null;
            string q = serialOrTag.Trim();
            var all = GetAllRecords();
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var r = all[i];
                if (string.Equals(r.Serial_Number, q, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(r.Tag, q, StringComparison.OrdinalIgnoreCase))
                {
                    return r;
                }
            }
            return null;
        }

        public void MarkRecordSynced(string recordId)
        {
            if (string.IsNullOrEmpty(recordId)) return;
            lock (_fileLock)
            {
                var records = GetAllRecords();
                bool modified = false;
                foreach (var r in records)
                {
                    if (r.Id == recordId)
                    {
                        r.IsSyncedToCloud = true;
                        r.SyncedAt = DateTime.Now;
                        modified = true;
                        break;
                    }
                }

                if (modified)
                {
                    try
                    {
                        var tempFile = _ledgerFilePath + ".tmp";
                        using (var sw = new StreamWriter(tempFile, false, Encoding.UTF8))
                        {
                            foreach (var r in records)
                            {
                                sw.WriteLine(JsonSerializer.Serialize(r));
                            }
                        }
                        File.Copy(tempFile, _ledgerFilePath, true);
                        File.Delete(tempFile);
                    }
                    catch { }
                }
            }
            RefreshPendingCount();
        }

        public void RefreshPendingCount()
        {
            int pending = 0;
            try
            {
                var records = GetAllRecords();
                pending = records.Count(r => !r.IsSyncedToCloud);
            }
            catch { }

            PendingCount = pending;
            PendingCountChanged?.Invoke(pending);
        }

        private async Task AutoSyncTimerCallback()
        {
            if (_isSyncing || PendingCount == 0) return;
            if (!NetworkInterface.GetIsNetworkAvailable()) return;

            await FlushPendingToCloudAsync();
        }

        public async Task FlushPendingToCloudAsync()
        {
            if (_isSyncing) return;
            _isSyncing = true;

            try
            {
                var records = GetAllRecords().Where(r => !r.IsSyncedToCloud).ToList();
                if (records.Count == 0) return;

                foreach (var rec in records)
                {
                    try
                    {
                        int.TryParse((rec.Battery_Health ?? "").Replace("%", "").Trim(), out int bHealth);
                        if (bHealth <= 0) bHealth = 100;

                        bool success = await GoogleSheetsDispatcher.DispatchItamRecordAsync(
                            string.IsNullOrWhiteSpace(rec.Tag) ? rec.Serial_Number : rec.Tag,
                            rec.Serial_Number,
                            rec.CPU_Model,
                            string.IsNullOrWhiteSpace(rec.Storage_Details) ? rec.RAM_GB : $"{rec.RAM_GB} / {rec.Storage_Details}",
                            bHealth,
                            rec.Storage_Health > 0 ? rec.Storage_Health : 100,
                            rec.Status,
                            string.IsNullOrWhiteSpace(rec.Work_In_Progress) ? "All Okay" : rec.Work_In_Progress,
                            rec.Physical_Grade,
                            rec.Technician_Notes,
                            rec.Technician,
                            rec.In_Date,
                            rec.Supplier,
                            rec.Out_Date,
                            rec.Customer
                        );

                        if (success)
                        {
                            MarkRecordSynced(rec.Id);
                        }
                    }
                    catch { }
                }
            }
            finally
            {
                _isSyncing = false;
                RefreshPendingCount();
            }
        }
    }

    public static class GoogleSheetsDispatcher
    {
        private static readonly System.Net.Http.HttpClient _httpClient = new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5
        })
        {
            Timeout = TimeSpan.FromSeconds(25)
        };

        public const string DefaultSheetsUrl = "https://script.google.com/macros/s/AKfycbyx4LIL1xbzTypuYKTUK2XuMVnLq8TRbdVsupEQlSjI0CxGQ3mG92yR7rY3bjq1EH4t/exec";

        public static async Task<bool> DispatchItamRecordAsync(
            string tag,
            string serialNumber,
            string processor,
            string memory,
            int batteryHealth,
            int storageHealth,
            string status,
            string workInProgress,
            string physicalGrade,
            string remarks,
            string technician,
            string inDate,
            string supplier,
            string outDate,
            string customer,
            string webhookUrl = DefaultSheetsUrl)
        {
            if (string.IsNullOrWhiteSpace(webhookUrl)) return false;

            try
            {
                var payload = new
                {
                    Tag              = tag ?? "",
                    Serial_Number    = serialNumber ?? "",
                    Processor        = processor ?? "",
                    Memory           = memory ?? "",
                    Battery_Health   = Math.Clamp(batteryHealth, 0, 100),
                    Storage_Health   = Math.Clamp(storageHealth, 0, 100),
                    Status           = status ?? "RTS",
                    Work_In_Progress = workInProgress ?? "All Okay",
                    Physical_Grade   = physicalGrade ?? "A+",
                    Remarks          = remarks ?? "",
                    Technician       = technician ?? "",
                    In_Date          = string.IsNullOrWhiteSpace(inDate) ? DateTime.Now.ToString("yyyy-MM-dd") : inDate,
                    Supplier         = supplier ?? "",
                    Out_Date         = outDate ?? "",
                    Customer         = customer ?? ""
                };

                string json = JsonSerializer.Serialize(payload);
                var content = new System.Net.Http.StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(webhookUrl, content);
                string body = await response.Content.ReadAsStringAsync();

                return response.IsSuccessStatusCode && (body.Contains("OK") || body.Contains("status\":\"OK\""));
            }
            catch
            {
                return false;
            }
        }

        public static async Task<bool> DispatchQcRecordAsync(
            string serialNumber,
            string model,
            string physicalGrade,
            string status,
            string notes,
            string cpu,
            string ram,
            string storage,
            string batteryHealth,
            string gpu,
            string webhookUrl = DefaultSheetsUrl)
        {
            int.TryParse((batteryHealth ?? "").Replace("%", "").Trim(), out int bHealth);
            if (bHealth <= 0) bHealth = 100;

            return await DispatchItamRecordAsync(
                serialNumber,
                serialNumber,
                cpu,
                $"{ram} / {storage}",
                bHealth,
                100,
                status,
                notes,
                physicalGrade,
                $"GPU: {gpu}",
                "OPERATOR",
                DateTime.Now.ToString("yyyy-MM-dd"),
                "Lease Return",
                "",
                "",
                webhookUrl
            );
        }
    }
}
