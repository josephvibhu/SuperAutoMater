using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SuperAutoMater.Wpf.Services;
using Velox.Shared.Dtos;
using VeloxBench.Core;
using VeloxBench.Services;

namespace SuperAutoMater.Wpf.Core
{
    /// <summary>
    /// Single durable sync outbox manager that displaces fragmented offline queues.
    /// Handles migration from legacy queues, enforces idempotency keys, and flushes events to cloud endpoints.
    /// </summary>
    public sealed class SyncOutboxDispatcher
    {
        private static readonly Lazy<SyncOutboxDispatcher> _instance =
            new Lazy<SyncOutboxDispatcher>(() => new SyncOutboxDispatcher());

        public static SyncOutboxDispatcher Instance => _instance.Value;

        private readonly QcRunStore _store;
        private readonly SemaphoreSlim _flushLock = new SemaphoreSlim(1, 1);
        private readonly Timer _syncTimer;
        private volatile bool _isSyncing = false;

        private static readonly HttpClient _httpClient = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5
        })
        {
            Timeout = TimeSpan.FromSeconds(25)
        };

        public event Action<int> OutboxCountChanged;

        public int PendingCount => _store.GetPendingOutboxCount();

        public SyncOutboxDispatcher(QcRunStore store = null)
        {
            _store = store ?? new QcRunStore();

            // Run one-time migration of legacy queues on boot
            MigrateLegacyQueues();

            // Poll every 30 seconds for background sync
            _syncTimer = new Timer(async _ => await BackgroundSyncCallback(), null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30));
        }

        public void MigrateLegacyQueues()
        {
            try
            {
                // 1. Migrate offline_sync_queue.json
                string queuePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "offline_sync_queue.json");
                if (File.Exists(queuePath))
                {
                    try
                    {
                        string json = File.ReadAllText(queuePath, Encoding.UTF8);
                        var legacyItems = JsonSerializer.Deserialize<List<AssetQueueRecord>>(json);
                        if (legacyItems != null && legacyItems.Count > 0)
                        {
                            int count = 0;
                            foreach (var item in legacyItems)
                            {
                                string key = $"legacy-queue-{item.Serial_Number}-{item.Timestamp}";
                                string payload = JsonSerializer.Serialize(item);
                                _store.EnqueueOutboxEvent("LegacySyncQueue", item.Serial_Number, key, payload);
                                count++;
                            }
                            AppLogger.Info($"Migrated {count} records from legacy offline_sync_queue.json into SQLite sync_outbox");
                        }
                        string backup = queuePath + ".migrated";
                        if (File.Exists(backup)) File.Delete(backup);
                        File.Move(queuePath, backup);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn("Failed to migrate offline_sync_queue.json", ex);
                    }
                }

                // 2. Migrate superautomater_ledger.jsonl
                string storageDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SuperAutoMater");
                string ledgerPath = Path.Combine(storageDir, "superautomater_ledger.jsonl");
                if (File.Exists(ledgerPath))
                {
                    try
                    {
                        var lines = File.ReadAllLines(ledgerPath, Encoding.UTF8);
                        int count = 0;
                        foreach (var line in lines)
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            try
                            {
                                var rec = JsonSerializer.Deserialize<QcAuditRecord>(line);
                                if (rec != null && !rec.IsSyncedToCloud)
                                {
                                    string key = $"legacy-ledger-{rec.Id}";
                                    string payload = JsonSerializer.Serialize(rec);
                                    _store.EnqueueOutboxEvent("LegacyLedgerRecord", rec.Serial_Number, key, payload);
                                    count++;
                                }
                            }
                            catch { }
                        }

                        if (count > 0)
                        {
                            AppLogger.Info($"Migrated {count} unsynced records from superautomater_ledger.jsonl into SQLite sync_outbox");
                        }
                        string backup = ledgerPath + ".migrated";
                        if (File.Exists(backup)) File.Delete(backup);
                        File.Move(ledgerPath, backup);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn("Failed to migrate superautomater_ledger.jsonl", ex);
                    }
                }

                OutboxCountChanged?.Invoke(PendingCount);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Error during legacy queue migration", ex);
            }
        }

        public async Task<int> FlushPendingAsync(string webhookUrl = null)
        {
            if (!await _flushLock.WaitAsync(100))
                return 0; // Already in progress

            _isSyncing = true;
            int dispatched = 0;

            try
            {
                var pendingEvents = _store.GetPendingOutboxEvents(50);
                if (pendingEvents.Count == 0) return 0;

                var cfg = BenchConfigService.Current;
                var licKey = LicenseService.Instance.LicenseKey;
                if (string.IsNullOrWhiteSpace(licKey)) licKey = cfg.LicenseKey;

                // Priority 1: Solid Authoritative Velox Cloud Backend (/api/v1/bench/sync)
                if (!string.IsNullOrWhiteSpace(cfg.ApiUrl) && string.IsNullOrWhiteSpace(webhookUrl))
                {
                    try
                    {
                        var batchItems = new List<BenchSyncItemDto>();
                        foreach (var evt in pendingEvents)
                        {
                            batchItems.Add(new BenchSyncItemDto
                            {
                                EventId = evt.Id,
                                EventType = evt.EventType,
                                AggregateId = evt.AggregateId,
                                IdempotencyKey = evt.IdempotencyKey,
                                Timestamp = evt.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                                PayloadJson = evt.PayloadJson
                            });
                        }

                        var batchReq = new BenchSyncBatchRequest
                        {
                            LicenseKey = licKey,
                            MachineName = cfg.BenchName ?? Environment.MachineName,
                            AppVersion = "1.6.10",
                            Events = batchItems
                        };

                        var syncUrl = $"{cfg.ApiUrl.TrimEnd('/')}/api/v1/bench/sync";
                        var reqMsg = new HttpRequestMessage(HttpMethod.Post, syncUrl);
                        reqMsg.Headers.Add("X-API-Key", cfg.ApiKey);
                        if (!string.IsNullOrWhiteSpace(licKey)) reqMsg.Headers.Add("X-License-Key", licKey);

                        var json = JsonSerializer.Serialize(batchReq);
                        reqMsg.Content = new StringContent(json, Encoding.UTF8, "application/json");

                        var resp = await _httpClient.SendAsync(reqMsg);
                        if (resp.IsSuccessStatusCode)
                        {
                            foreach (var evt in pendingEvents)
                            {
                                _store.MarkOutboxEventDispatched(evt.Id);
                                dispatched++;
                            }
                            AppLogger.Info($"[CloudSync] Dispatched batch of {dispatched} QC events to Velox Cloud API.");
                            return dispatched;
                        }
                        else
                        {
                            var errBody = await resp.Content.ReadAsStringAsync();
                            AppLogger.Warn($"[CloudSync] Cloud sync rejected (HTTP {resp.StatusCode}): {errBody}");
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn($"[CloudSync] Cloud sync request failed: {ex.Message}");
                    }
                }

                // Priority 2: Fallback to individual dispatch (Legacy Google Sheets or Custom Webhook)
                string fallbackUrl = webhookUrl ?? GoogleSheetsDispatcher.DefaultSheetsUrl;
                if (!string.IsNullOrWhiteSpace(fallbackUrl))
                {
                    foreach (var evt in pendingEvents)
                    {
                        try
                        {
                            bool success = await DispatchEventAsync(evt, fallbackUrl);
                            if (success)
                            {
                                _store.MarkOutboxEventDispatched(evt.Id);
                                dispatched++;
                            }
                            else
                            {
                                _store.RecordOutboxAttemptFailure(evt.Id, "Server returned non-success response");
                            }
                        }
                        catch (Exception ex)
                        {
                            _store.RecordOutboxAttemptFailure(evt.Id, ex.Message);
                            AppLogger.Warn($"Failed to dispatch outbox event {evt.Id}", ex);
                        }
                    }
                }
            }
            finally
            {
                _isSyncing = false;
                _flushLock.Release();
                OutboxCountChanged?.Invoke(PendingCount);
            }

            return dispatched;
        }

        private async Task<bool> DispatchEventAsync(OutboxEventRecord evt, string webhookUrl)
        {
            // Build standardized cloud dispatch payload
            var dispatchEnvelope = new
            {
                Event_Id        = evt.Id,
                Event_Type      = evt.EventType,
                Aggregate_Id    = evt.AggregateId,
                Idempotency_Key = evt.IdempotencyKey,
                Timestamp       = evt.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm:ss"),
                Payload         = evt.PayloadJson
            };

            string json = JsonSerializer.Serialize(dispatchEnvelope);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(webhookUrl, content);
            string body = await response.Content.ReadAsStringAsync();

            return response.IsSuccessStatusCode && (body.Contains("OK") || body.Contains("status\":\"OK\""));
        }

        private async Task BackgroundSyncCallback()
        {
            if (_isSyncing || PendingCount == 0) return;
            if (!NetworkInterface.GetIsNetworkAvailable()) return;

            await FlushPendingAsync();
        }
    }
}
