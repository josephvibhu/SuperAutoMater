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

        public const string DefaultSheetsUrl = "https://script.google.com/macros/s/AKfycbxh1-pzrBC1DieKrlM55_TsIfjP5sKoTfCdPuJ_PkMDe5E1HXwY1przZkejTBnuWm2_DQ/exec";

        public static string GetActiveWebhookUrl()
        {
            try
            {
                // Check 1: sheets_url.txt beside the executable
                string localFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sheets_url.txt");
                if (File.Exists(localFile))
                {
                    string url = File.ReadAllText(localFile).Trim();
                    if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https"))
                        return url;
                }

                // Check 2: sheets_url.txt in LocalApplicationData
                string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SuperAutoMater");
                string appDataFile = Path.Combine(appDataDir, "sheets_url.txt");
                if (File.Exists(appDataFile))
                {
                    string url = File.ReadAllText(appDataFile).Trim();
                    if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https"))
                        return url;
                }
            }
            catch { }
            return DefaultSheetsUrl;
        }

        public static void SaveActiveWebhookUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            string trimmed = url.Trim();
            try
            {
                string localFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sheets_url.txt");
                File.WriteAllText(localFile, trimmed);
            }
            catch { }

            try
            {
                string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SuperAutoMater");
                if (!Directory.Exists(appDataDir)) Directory.CreateDirectory(appDataDir);
                string appDataFile = Path.Combine(appDataDir, "sheets_url.txt");
                File.WriteAllText(appDataFile, trimmed);
            }
            catch { }
        }

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

        public async Task<int> FlushQueueAsync(string webhookUrl = null)
        {
            if (string.IsNullOrWhiteSpace(webhookUrl))
                webhookUrl = GetActiveWebhookUrl();

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
                            Assigned_To      = record.Assigned_To,
                            Intake_Tech      = record.Intake_Tech,
                            Service_Tech     = record.Service_Tech,
                            QC_Tech          = record.QC_Tech,
                            Approval_Tech    = record.Approval_Tech,
                            Missing_Components = record.Missing_Components,
                            External_Vendor  = record.External_Vendor,
                            QC_Profile       = record.QC_Profile,
                            In_Date          = record.In_Date,
                            Supplier         = record.Supplier,
                            Out_Date         = record.Out_Date,
                            Customer         = record.Customer,
                            Model            = record.Model,
                            Shelf_Location   = record.Shelf_Location,
                            Timestamp        = record.Timestamp
                        });


                        var req = new HttpRequestMessage(HttpMethod.Post, webhookUrl)
                        {
                            Content = new StringContent(json, Encoding.UTF8, "application/json")
                        };
                        var response = await SendWithGoogleRedirectAsync(req);
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

        public async Task<AssetQueueRecord> QueryRemoteSheetAsync(string serialOrTag, string webhookUrl = null)
        {
            if (string.IsNullOrWhiteSpace(serialOrTag))
                return null;

            if (string.IsNullOrWhiteSpace(webhookUrl))
                webhookUrl = GetActiveWebhookUrl();

            try
            {
                string queryUrl = $"{webhookUrl}?q={Uri.EscapeDataString(serialOrTag.Trim())}";
                var req = new HttpRequestMessage(HttpMethod.Get, queryUrl);
                var response = await SendWithGoogleRedirectAsync(req);
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

        private static async Task<HttpResponseMessage> SendWithGoogleRedirectAsync(HttpRequestMessage request)
        {
            using var noRedirectClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                Timeout = TimeSpan.FromSeconds(25)
            };

            var response = await noRedirectClient.SendAsync(request);
            if (response.StatusCode == System.Net.HttpStatusCode.Found ||
                response.StatusCode == System.Net.HttpStatusCode.Redirect ||
                response.StatusCode == System.Net.HttpStatusCode.SeeOther)
            {
                var redirectUrl = response.Headers.Location?.ToString();
                if (!string.IsNullOrEmpty(redirectUrl))
                {
                    using var getClient = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
                    return await getClient.GetAsync(redirectUrl);
                }
            }
            return response;
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
        public string Assigned_To { get; set; } = "";
        public string Intake_Tech { get; set; } = "";
        public string Service_Tech { get; set; } = "";
        public string QC_Tech { get; set; } = "";
        public string Approval_Tech { get; set; } = "";
        public string Missing_Components { get; set; } = "";
        public string External_Vendor { get; set; } = "";
        public string QC_Profile { get; set; } = "Full Diagnostic";
        public string In_Date { get; set; } = "";
        public string Supplier { get; set; } = "";
        public string Out_Date { get; set; } = "";
        public string Customer { get; set; } = "";
        public string Timestamp { get; set; } = "";

        public DateTime QueuedAt { get; set; } = DateTime.Now;
    }
}
