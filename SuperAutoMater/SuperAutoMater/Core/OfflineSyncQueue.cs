using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SuperAutoMater.Wpf.Core;

namespace SuperAutoMater
{
    /// <summary>
    /// Persistent Offline Cloud Sync Queue.
    /// Safely stores asset records locally when offline and automatically flushes them
    /// to the Google Sheets webhook when network connectivity is restored.
    /// </summary>
    public sealed class OfflineSyncQueue
    {
        private static readonly Lazy<OfflineSyncQueue> _instance =
            new Lazy<OfflineSyncQueue>(() => new OfflineSyncQueue());

        public static OfflineSyncQueue Instance => _instance.Value;

        public const string DefaultSheetsUrl = "https://script.google.com/macros/s/AKfycbyx4LIL1xbzTypuYKTUK2XuMVnLq8TRbdVsupEQlSjI0CxGQ3mG92yR7rY3bjq1EH4t/exec";

        private readonly string _queueFilePath;
        private readonly object _fileLock = new object();
        private readonly SemaphoreSlim _flushLock = new SemaphoreSlim(1, 1);
        private readonly List<AssetQueueRecord> _items = new List<AssetQueueRecord>();

        private static readonly HttpClient _httpClient = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5
        })
        {
            Timeout = TimeSpan.FromSeconds(25)
        };

        public event Action<int> QueueChanged;

        public int PendingCount
        {
            get
            {
                lock (_fileLock)
                {
                    return _items.Count;
                }
            }
        }

        private OfflineSyncQueue()
        {
            _queueFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "offline_sync_queue.json");
            LoadFromDisk();
        }

        public void Enqueue(AssetQueueRecord record)
        {
            if (record == null) return;
            lock (_fileLock)
            {
                record.QueuedAt = DateTime.Now;
                // Avoid exact duplicate serial number in queue
                _items.RemoveAll(i => string.Equals(i.Serial_Number, record.Serial_Number, StringComparison.OrdinalIgnoreCase));
                _items.Add(record);
                SaveToDisk();
            }
            QueueChanged?.Invoke(PendingCount);
        }

        public List<AssetQueueRecord> GetPendingItems()
        {
            lock (_fileLock)
            {
                return new List<AssetQueueRecord>(_items);
            }
        }

        public async Task<int> FlushQueueAsync(string webhookUrl)
        {
            if (string.IsNullOrWhiteSpace(webhookUrl) || !webhookUrl.StartsWith("https://script.google.com/macros/s/"))
                return 0;

            if (!await _flushLock.WaitAsync(100))
                return 0; // Already flushing

            int syncedCount = 0;
            try
            {
                List<AssetQueueRecord> snapshot;
                lock (_fileLock)
                {
                    if (_items.Count == 0) return 0;
                    snapshot = new List<AssetQueueRecord>(_items);
                }

                var remaining = new List<AssetQueueRecord>();

                foreach (var record in snapshot)
                {
                    try
                    {
                        string json = JsonSerializer.Serialize(new
                        {
                            Tag              = string.IsNullOrWhiteSpace(record.Tag) ? record.Asset_Tag : record.Tag,
                            Serial_Number    = record.Serial_Number,
                            Processor        = record.Processor,
                            Memory           = record.Memory,
                            Battery_Health   = record.Battery_Health,
                            Storage_Health   = record.Storage_Health,
                            Status           = record.Status,
                            Work_In_Progress = record.Work_In_Progress,
                            Physical_Grade   = record.Physical_Grade,
                            Remarks          = record.Remarks,
                            Technician       = record.Technician,
                            In_Date          = record.In_Date,
                            Supplier         = record.Supplier,
                            Out_Date         = record.Out_Date,
                            Customer         = record.Customer,
                            Model            = record.Model,
                            Shelf_Location   = record.Shelf_Location,
                            Timestamp        = record.Timestamp
                        });

                        var content = new StringContent(json, Encoding.UTF8, "application/json");
                        var response = await _httpClient.PostAsync(webhookUrl, content);
                        string body = await response.Content.ReadAsStringAsync();

                        if (response.IsSuccessStatusCode && (body.Contains("OK") || body.Contains("status\":\"OK\"")))
                        {
                            syncedCount++;
                        }
                        else
                        {
                            remaining.Add(record);
                            AppLogger.Warn($"Google Sheets webhook responded with status {response.StatusCode}: {body}");
                        }
                    }
                    catch (Exception ex)
                    {
                        // Network error or timeout, retain in queue
                        remaining.Add(record);
                        AppLogger.Warn($"OfflineSyncQueue flush failed for asset {record.Serial_Number}: {ex.Message}");
                    }
                }

                lock (_fileLock)
                {
                    _items.Clear();
                    _items.AddRange(remaining);
                    SaveToDisk();
                }

                if (syncedCount > 0)
                {
                    QueueChanged?.Invoke(PendingCount);
                }
            }
            finally
            {
                _flushLock.Release();
            }

            return syncedCount;
        }

        private void LoadFromDisk()
        {
            lock (_fileLock)
            {
                try
                {
                    if (File.Exists(_queueFilePath))
                    {
                        string json = File.ReadAllText(_queueFilePath);
                        var loaded = JsonSerializer.Deserialize<List<AssetQueueRecord>>(json);
                        _items.Clear();
                        if (loaded != null) _items.AddRange(loaded);
                    }
                }
                catch { }
            }
        }

        private void SaveToDisk()
        {
            try
            {
                string json = JsonSerializer.Serialize(_items, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_queueFilePath, json);
            }
            catch { }
        }

        public async Task<AssetQueueRecord> QueryRemoteSheetAsync(string serialOrTag, string webhookUrl = DefaultSheetsUrl)
        {
            if (string.IsNullOrWhiteSpace(serialOrTag) || string.IsNullOrWhiteSpace(webhookUrl))
                return null;

            try
            {
                string queryUrl = $"{webhookUrl}?q={Uri.EscapeDataString(serialOrTag.Trim())}";
                var response = await _httpClient.GetAsync(queryUrl);
                if (!response.IsSuccessStatusCode) return null;

                string body = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (!root.TryGetProperty("found", out var propFound) || !propFound.GetBoolean())
                    return null;

                int bHealth = 100;
                if (root.TryGetProperty("Battery_Health", out var bProp))
                {
                    if (bProp.ValueKind == JsonValueKind.Number) bHealth = bProp.GetInt32();
                    else int.TryParse(bProp.GetString(), out bHealth);
                }

                int sHealth = 100;
                if (root.TryGetProperty("Storage_Health", out var sProp))
                {
                    if (sProp.ValueKind == JsonValueKind.Number) sHealth = sProp.GetInt32();
                    else int.TryParse(sProp.GetString(), out sHealth);
                }

                return new AssetQueueRecord
                {
                    Tag = root.TryGetProperty("Tag", out var pTag) ? pTag.GetString() ?? "" : "",
                    Serial_Number = root.TryGetProperty("Serial_Number", out var pSn) ? pSn.GetString() ?? "" : "",
                    Processor = root.TryGetProperty("Processor", out var pCpu) ? pCpu.GetString() ?? "" : "",
                    Memory = root.TryGetProperty("Memory", out var pMem) ? pMem.GetString() ?? "" : "",
                    Battery_Health = bHealth,
                    Storage_Health = sHealth,
                    Status = root.TryGetProperty("Status", out var pStat) ? pStat.GetString() ?? "RTS" : "RTS",
                    Work_In_Progress = root.TryGetProperty("Work_In_Progress", out var pWip) ? pWip.GetString() ?? "All Okay" : "All Okay",
                    Physical_Grade = root.TryGetProperty("Physical_Grade", out var pGrd) ? pGrd.GetString() ?? "A+" : "A+",
                    Remarks = root.TryGetProperty("Remarks", out var pRem) ? pRem.GetString() ?? "" : "",
                    Technician = root.TryGetProperty("Technician", out var pTech) ? pTech.GetString() ?? "" : "",
                    In_Date = root.TryGetProperty("In_Date", out var pIn) ? pIn.GetString() ?? "" : "",
                    Supplier = root.TryGetProperty("Supplier", out var pSup) ? pSup.GetString() ?? "" : "",
                    Out_Date = root.TryGetProperty("Out_Date", out var pOut) ? pOut.GetString() ?? "" : "",
                    Customer = root.TryGetProperty("Customer", out var pCust) ? pCust.GetString() ?? "" : ""
                };
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to query Google Sheet for '{serialOrTag}': {ex.Message}");
                return null;
            }
        }

        public AssetQueueRecord FindLocalQueuedRecord(string serialOrTag)
        {
            if (string.IsNullOrWhiteSpace(serialOrTag)) return null;
            string q = serialOrTag.Trim();
            lock (_fileLock)
            {
                for (int i = _items.Count - 1; i >= 0; i--)
                {
                    var item = _items[i];
                    if (string.Equals(item.Serial_Number, q, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(item.Tag, q, StringComparison.OrdinalIgnoreCase))
                    {
                        return item;
                    }
                }
            }
            return null;
        }
    }

    public class AssetQueueRecord
    {
        public string Tag { get; set; } = "";
        public string Asset_Tag
        {
            get => string.IsNullOrEmpty(Tag) ? _assetTag : Tag;
            set { _assetTag = value; if (string.IsNullOrEmpty(Tag)) Tag = value; }
        }
        private string _assetTag = "";

        public string Serial_Number { get; set; } = "";
        public string Model { get; set; } = "";
        public string Processor { get; set; } = "";
        public string Memory { get; set; } = ""; // [ram/storage]
        public int Battery_Health { get; set; } = 100; // in 0-100 without percent symbol
        public int Storage_Health { get; set; } = 100; // in 0-100 without percent symbol
        public string Status { get; set; } = "RTS"; // RTS, WIP, SOLD, RFR, DEMO, RENT
        public string Work_In_Progress { get; set; } = "All Okay"; // All Okay, No OS, Power On issue, Undefined, keyboard issue, battery issue, bios issue, camera issue, multiple issue, etc..
        public string Wip_Issue
        {
            get => Work_In_Progress;
            set => Work_In_Progress = value;
        }
        public string Physical_Grade { get; set; } = "A+"; // A+, A, B, C, D
        public string Remarks { get; set; } = "";
        public string Shelf_Location { get; set; } = "";
        public string Technician { get; set; } = "TECH-01"; // Technician Name or ID
        public string In_Date { get; set; } = "";
        public string Supplier { get; set; } = "";
        public string Out_Date { get; set; } = "";
        public string Customer { get; set; } = "";
        public string Timestamp { get; set; } = "";
        public DateTime QueuedAt { get; set; } = DateTime.Now;
    }
}
