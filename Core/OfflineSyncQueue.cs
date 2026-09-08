using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ITAS_QC_Tool
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
                            Asset_Tag      = record.Asset_Tag,
                            Serial_Number  = record.Serial_Number,
                            Model          = record.Model,
                            Processor      = record.Processor,
                            Memory         = record.Memory,
                            Battery_Health = record.Battery_Health,
                            Status         = record.Status,
                            Wip_Issue      = record.Wip_Issue,
                            Physical_Grade = record.Physical_Grade,
                            Remarks        = record.Remarks,
                            Shelf_Location = record.Shelf_Location,
                            Timestamp      = record.Timestamp
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
        public string Asset_Tag { get; set; } = "";
        public string Serial_Number { get; set; } = "";
        public string Model { get; set; } = "";
        public string Processor { get; set; } = "";
        public string Memory { get; set; } = "";
        public int Battery_Health { get; set; } = 100;
        public string Status { get; set; } = "RTS";
        public string Wip_Issue { get; set; } = "All Okay";
        public string Physical_Grade { get; set; } = "A+";
        public string Remarks { get; set; } = "";
        public string Shelf_Location { get; set; } = "";
        public string Timestamp { get; set; } = "";
        public DateTime QueuedAt { get; set; } = DateTime.Now;
    }
}
