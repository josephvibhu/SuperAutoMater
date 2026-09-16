using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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
                            tag              = string.IsNullOrWhiteSpace(record.Tag) ? record.Asset_Tag : record.Tag,
                            Asset_Tag        = string.IsNullOrWhiteSpace(record.Tag) ? record.Asset_Tag : record.Tag,
                            Serial_Number    = record.Serial_Number,
                            serial_number    = record.Serial_Number,
                            Processor        = record.Processor,
                            processor        = record.Processor,
                            Memory           = record.Memory,
                            memory           = record.Memory,
                            Battery_Health   = record.Battery_Health,
                            battery_health   = record.Battery_Health,
                            Storage_Health   = record.Storage_Health,
                            storage_health   = record.Storage_Health,
                            Status           = record.Status,
                            status           = record.Status,
                            Work_In_Progress = record.Work_In_Progress,
                            work_in_progress = record.Work_In_Progress,
                            Wip_Issue        = record.Work_In_Progress,
                            Physical_Grade   = record.Physical_Grade,
                            physical_grade   = record.Physical_Grade,
                            Remarks          = record.Remarks,
                            remarks          = record.Remarks,
                            Technician       = record.Technician,
                            technician       = record.Technician,
                            In_Date          = record.In_Date,
                            in_date          = record.In_Date,
                            Supplier         = record.Supplier,
                            supplier         = record.Supplier,
                            Out_Date         = record.Out_Date,
                            out_date         = record.Out_Date,
                            Customer         = record.Customer,
                            customer         = record.Customer,
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
                        }
                    }
                    catch
                    {
                        // Network error, retain in queue
                        remaining.Add(record);
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
