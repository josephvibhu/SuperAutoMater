using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using SuperAutoMater.Wpf.Core;

namespace SuperAutoMater.Wpf.Services
{
    public sealed class CsvIntakeItem
    {
        public string SerialNumber { get; set; } = "";
        public string AssetTag { get; set; } = "";
        public string Model { get; set; } = "";
        public string BatchId { get; set; } = "";
        public string InitialLocation { get; set; } = "INTAKE-STAGING";
        public string Supplier { get; set; } = "";
        public string Customer { get; set; } = "";
        public string ChargerStatus { get; set; } = "NoChargerMissing";
        public string TestProfileId { get; set; } = "standard-refurb-v1";
        public string Technician { get; set; } = "BULK_CSV_INTAKE";
    }

    public sealed class QuarantinedIntakeItem
    {
        public int RowIndex { get; set; }
        public string RawRow { get; set; } = "";
        public string Reason { get; set; } = "";
        public CsvIntakeItem ParsedItem { get; set; }
    }

    public sealed class IntakeBatchReport
    {
        public string BatchId { get; set; } = Guid.NewGuid().ToString("N");
        public int TotalItems { get; set; }
        public int AcceptedCount { get; set; }
        public int QuarantinedCount { get; set; }
        public List<string> AcceptedAssetIds { get; set; } = new List<string>();
        public List<QuarantinedIntakeItem> QuarantinedItems { get; set; } = new List<QuarantinedIntakeItem>();
        public DateTimeOffset ProcessedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Customer-Selected Integration: Bidirectional ITAM / CSV & ERP/WMS Connector.
    /// Manages bulk device manifest ingestion, defect/blacklist quarantine filtering,
    /// and outbound 15-column ITAM reconciliation dispatch.
    /// </summary>
    public sealed class ItamCsvConnectorService
    {
        private readonly QcRunStore _store;

        public ItamCsvConnectorService(QcRunStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <summary>
        /// Parses and validates a bulk intake CSV manifest.
        /// Supported header names (case-insensitive):
        /// "Serial Number" / "Serial", "Asset Tag" / "Tag", "Model", "Batch Id" / "Batch",
        /// "Location", "Supplier", "Customer", "Charger Status", "Test Profile", "Technician".
        /// </summary>
        public IntakeBatchReport ProcessBulkCsvIntake(string csvContent, string batchId = null, string defaultTechnician = "INTAKE_AGENT")
        {
            var report = new IntakeBatchReport
            {
                BatchId = string.IsNullOrWhiteSpace(batchId) ? $"BATCH-{DateTime.UtcNow:yyyyMMdd-HHmmss}" : batchId.Trim()
            };

            if (string.IsNullOrWhiteSpace(csvContent))
                return report;

            using var reader = new StringReader(csvContent);
            string headerLine = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(headerLine))
                return report;

            var headers = ParseCsvLine(headerLine);
            var colMap = MapHeaders(headers);

            int rowIndex = 1;
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                rowIndex++;
                if (string.IsNullOrWhiteSpace(line)) continue;

                report.TotalItems++;
                var parts = ParseCsvLine(line);
                var item = ExtractItem(parts, colMap, report.BatchId, defaultTechnician);

                // 1. Validation: Must have at least a Serial or Asset Tag
                if (string.IsNullOrWhiteSpace(item.SerialNumber) && string.IsNullOrWhiteSpace(item.AssetTag))
                {
                    report.QuarantinedCount++;
                    report.QuarantinedItems.Add(new QuarantinedIntakeItem
                    {
                        RowIndex = rowIndex,
                        RawRow = line,
                        Reason = "Missing both Serial Number and Asset Tag.",
                        ParsedItem = item
                    });
                    continue;
                }

                // 2. Validation: Check generic/blacklisted serial numbers
                if (!string.IsNullOrWhiteSpace(item.SerialNumber) && QcRunStore.IsGenericSerial(item.SerialNumber))
                {
                    if (string.IsNullOrWhiteSpace(item.AssetTag))
                    {
                        report.QuarantinedCount++;
                        report.QuarantinedItems.Add(new QuarantinedIntakeItem
                        {
                            RowIndex = rowIndex,
                            RawRow = line,
                            Reason = $"Generic / blacklisted serial number '{item.SerialNumber}' without unique Asset Tag.",
                            ParsedItem = item
                        });
                        continue;
                    }
                }

                // 3. Acceptance & Record Ingestion
                try
                {
                    var req = new AssetIntakeRequest
                    {
                        SerialNumber = item.SerialNumber,
                        AssetTag = item.AssetTag,
                        Model = item.Model,
                        IntakeBatchId = report.BatchId,
                        InitialLocation = string.IsNullOrWhiteSpace(item.InitialLocation) ? "INTAKE-STAGING" : item.InitialLocation,
                        TestProfileId = item.TestProfileId,
                        Technician = item.Technician,
                        Notes = string.IsNullOrWhiteSpace(item.Supplier) ? "CSV-Bulk-Intake" : $"Supplier: {item.Supplier}"
                    };

                    string assetId = _store.IntakeAsset(req);

                    report.AcceptedCount++;
                    report.AcceptedAssetIds.Add(assetId);
                }
                catch (Exception ex)
                {
                    report.QuarantinedCount++;
                    report.QuarantinedItems.Add(new QuarantinedIntakeItem
                    {
                        RowIndex = rowIndex,
                        RawRow = line,
                        Reason = $"Database ingestion error: {ex.Message}",
                        ParsedItem = item
                    });
                }
            }

            // Save manifest audit record
            _store.SaveIntegrationManifest(new IntegrationManifestRecord
            {
                Direction = "Inbound",
                ConnectorType = "ITAM_CSV",
                BatchId = report.BatchId,
                TotalItems = report.TotalItems,
                AcceptedItems = report.AcceptedCount,
                QuarantinedItems = report.QuarantinedCount,
                ManifestJson = JsonSerializer.Serialize(new
                {
                    report.BatchId,
                    report.TotalItems,
                    report.AcceptedCount,
                    report.QuarantinedCount,
                    QuarantinedReasons = report.QuarantinedItems.Select(q => new { q.RowIndex, q.Reason }).ToList()
                })
            });

            return report;
        }

        /// <summary>
        /// Generates an outbound 15-column ITAM reconciliation CSV manifest for processed units.
        /// Columns:
        /// tag, Serial Number, Processor, Memory [ram/storage], Battery health, Storage Health, Status, Work in Progress, Physical Grade, Remarks, Technician Name or ID, In Date, Supplier, Out Date, Customer
        /// </summary>
        public string ExportItamReconciliationCsv(IEnumerable<AssetWipRecord> assets, string batchId = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("tag,Serial Number,Processor,Memory [ram/storage],Battery health,Storage Health,Status,Work in Progress,Physical Grade,Remarks,Technician Name or ID,In Date,Supplier,Out Date,Customer");

            var assetList = assets?.ToList() ?? new List<AssetWipRecord>();
            foreach (var a in assetList)
            {
                string tag = EscapeCsv(a.AssetTag);
                string serial = EscapeCsv(a.SerialNumber);
                string processor = EscapeCsv(a.Model); // Default hardware model/processor
                string memory = "N/A"; // Provided via summary or benchmark metrics
                string battHealth = "100";
                string storageHealth = a.StorageHealth.ToString(CultureInfo.InvariantCulture);
                string status = a.LifecycleQueue switch
                {
                    AssetQueueStatus.ReadyForRelease => "RTS",
                    AssetQueueStatus.Disposed => "SOLD",
                    _ => "WIP"
                };
                string wip = EscapeCsv(string.IsNullOrWhiteSpace(a.WorkInProgress) ? "All Okay" : a.WorkInProgress);
                string grade = EscapeCsv(string.IsNullOrWhiteSpace(a.LatestRunGrade) ? "A" : a.LatestRunGrade);
                string remarks = EscapeCsv(a.Remarks);
                string tech = EscapeCsv(string.IsNullOrWhiteSpace(a.LatestRunId) ? "TECH-01" : "Technician");
                string inDate = EscapeCsv(string.IsNullOrWhiteSpace(a.InDate) ? a.CreatedAtUtc.ToString("yyyy-MM-dd") : a.InDate);
                string supplier = EscapeCsv(a.Supplier);
                string outDate = EscapeCsv(a.OutDate);
                string customer = EscapeCsv(a.Customer);

                sb.AppendLine($"{tag},{serial},{processor},{memory},{battHealth},{storageHealth},{status},{wip},{grade},{remarks},{tech},{inDate},{supplier},{outDate},{customer}");
            }

            string csvOutput = sb.ToString();

            // Record outbound manifest
            _store.SaveIntegrationManifest(new IntegrationManifestRecord
            {
                Direction = "Outbound",
                ConnectorType = "ITAM_CSV",
                BatchId = string.IsNullOrWhiteSpace(batchId) ? $"OUT-{DateTime.UtcNow:yyyyMMdd-HHmmss}" : batchId,
                TotalItems = assetList.Count,
                AcceptedItems = assetList.Count,
                QuarantinedItems = 0,
                ManifestJson = JsonSerializer.Serialize(new
                {
                    ExportCount = assetList.Count,
                    GeneratedAtUtc = DateTimeOffset.UtcNow
                })
            });

            return csvOutput;
        }

        private static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            var current = new StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '\"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '\"')
                    {
                        current.Append('\"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            result.Add(current.ToString().Trim());
            return result;
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r"))
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }
            return value;
        }

        private static Dictionary<string, int> MapHeaders(List<string> headers)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Count; i++)
            {
                string h = headers[i].Trim();
                if (h.Equals("Serial Number", StringComparison.OrdinalIgnoreCase) || h.Equals("Serial", StringComparison.OrdinalIgnoreCase) || h.Equals("SN", StringComparison.OrdinalIgnoreCase))
                    map["serial"] = i;
                else if (h.Equals("Asset Tag", StringComparison.OrdinalIgnoreCase) || h.Equals("Tag", StringComparison.OrdinalIgnoreCase))
                    map["tag"] = i;
                else if (h.Equals("Model", StringComparison.OrdinalIgnoreCase) || h.Equals("Processor", StringComparison.OrdinalIgnoreCase))
                    map["model"] = i;
                else if (h.Equals("Batch Id", StringComparison.OrdinalIgnoreCase) || h.Equals("Batch", StringComparison.OrdinalIgnoreCase))
                    map["batch"] = i;
                else if (h.Equals("Location", StringComparison.OrdinalIgnoreCase) || h.Equals("Bay", StringComparison.OrdinalIgnoreCase))
                    map["location"] = i;
                else if (h.Equals("Supplier", StringComparison.OrdinalIgnoreCase) || h.Equals("Source", StringComparison.OrdinalIgnoreCase))
                    map["supplier"] = i;
                else if (h.Equals("Customer", StringComparison.OrdinalIgnoreCase))
                    map["customer"] = i;
                else if (h.Equals("Charger Status", StringComparison.OrdinalIgnoreCase) || h.Equals("Charger", StringComparison.OrdinalIgnoreCase))
                    map["charger"] = i;
                else if (h.Equals("Test Profile", StringComparison.OrdinalIgnoreCase) || h.Equals("Profile", StringComparison.OrdinalIgnoreCase))
                    map["profile"] = i;
                else if (h.Equals("Technician", StringComparison.OrdinalIgnoreCase) || h.Equals("Tech", StringComparison.OrdinalIgnoreCase))
                    map["tech"] = i;
            }
            return map;
        }

        private static CsvIntakeItem ExtractItem(List<string> parts, Dictionary<string, int> map, string defaultBatchId, string defaultTechnician)
        {
            string Get(string key) => map.TryGetValue(key, out int idx) && idx < parts.Count ? parts[idx] : "";

            return new CsvIntakeItem
            {
                SerialNumber = Get("serial"),
                AssetTag = Get("tag"),
                Model = Get("model"),
                BatchId = string.IsNullOrWhiteSpace(Get("batch")) ? defaultBatchId : Get("batch"),
                InitialLocation = string.IsNullOrWhiteSpace(Get("location")) ? "INTAKE-STAGING" : Get("location"),
                Supplier = Get("supplier"),
                Customer = Get("customer"),
                ChargerStatus = string.IsNullOrWhiteSpace(Get("charger")) ? "NoChargerMissing" : Get("charger"),
                TestProfileId = string.IsNullOrWhiteSpace(Get("profile")) ? "standard-refurb-v1" : Get("profile"),
                Technician = string.IsNullOrWhiteSpace(Get("tech")) ? defaultTechnician : Get("tech")
            };
        }
    }
}
