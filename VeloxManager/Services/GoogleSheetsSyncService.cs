using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VeloxManager.ViewModels;

namespace VeloxManager.Services;

public sealed class GoogleSheetsSyncResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public string? BackupFilePath { get; init; }
    public int ExportedCount { get; init; }
    public DateTimeOffset SyncedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class GoogleSheetsSyncService
{
    private static readonly HttpClient HttpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 10
    })
    {
        Timeout = TimeSpan.FromSeconds(45)
    };

    private static readonly string BackupDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backups");
    private static readonly string LegacyUrlFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sheets_url.txt");

    public static string GetConfiguredWebhookUrl()
    {
        var configUrl = ManagerConfigService.Current.GoogleSheetsWebhookUrl;
        if (!string.IsNullOrWhiteSpace(configUrl)) return configUrl.Trim();

        if (File.Exists(LegacyUrlFile))
        {
            try
            {
                var txt = File.ReadAllText(LegacyUrlFile).Trim();
                if (!string.IsNullOrWhiteSpace(txt)) return txt;
            }
            catch { }
        }

        return "";
    }

    public static void SaveWebhookUrl(string url)
    {
        var trimmed = url?.Trim() ?? "";
        ManagerConfigService.Current.GoogleSheetsWebhookUrl = trimmed;
        ManagerConfigService.Save();

        try
        {
            File.WriteAllText(LegacyUrlFile, trimmed);
        }
        catch { }
    }

    /// <summary>
    /// Exports the provided assets into the standard 15-column ITAM reconciliation CSV format
    /// and dispatches to the configured Google Apps Script webhook URL (or local backup if URL is empty).
    /// </summary>
    public static async Task<GoogleSheetsSyncResult> SyncAssetsAsync(
        IReadOnlyList<AssetItemViewModel> assets,
        CancellationToken ct = default)
    {
        if (assets == null || assets.Count == 0)
        {
            return new GoogleSheetsSyncResult
            {
                Success = false,
                Message = "No assets available in memory to sync."
            };
        }

        Directory.CreateDirectory(BackupDir);

        // 1. Build 16-Column CSV Representation
        var sb = new StringBuilder();
        sb.AppendLine("tag,serial,model,processor,memory,battHealth,storageHealth,status,wip,grade,remarks,tech,inDate,supplier,outDate,customer");

        var rowsPayload = new List<Dictionary<string, object>>();

        foreach (var a in assets)
        {
            var raw = a.Model;
            var tag = EscapeCsv(raw.AssetTag);
            var serial = EscapeCsv(raw.SerialNumber);
            var model = EscapeCsv(raw.Model);
            var processor = EscapeCsv(ExtractProcessorNotes(raw.Notes));
            var memory = EscapeCsv(ExtractMemoryNotes(raw.Notes));
            var battHealth = raw.BatteryHealth.HasValue ? $"{raw.BatteryHealth.Value}%" : "";
            var storageHealth = raw.StorageHealth.HasValue ? $"{raw.StorageHealth.Value}%" : "";
            var status = raw.LifecycleQueue switch
            {
                Velox.Shared.Models.LifecycleQueue.ReadyForSale => "RTS",
                Velox.Shared.Models.LifecycleQueue.Sold => "SOLD",
                Velox.Shared.Models.LifecycleQueue.Disposed => "SCRAP",
                _ => "WIP"
            };
            var wip = EscapeCsv(string.IsNullOrWhiteSpace(raw.MissingComponents) ? "All Okay" : raw.MissingComponents);
            var grade = EscapeCsv(string.IsNullOrWhiteSpace(raw.CosmeticGrade) ? raw.Grade.ToString() : raw.CosmeticGrade);
            var remarks = EscapeCsv(raw.Notes);
            var tech = EscapeCsv(string.IsNullOrWhiteSpace(a.AssignedEmployeeName) ? "Unassigned" : a.AssignedEmployeeName);
            var inDate = EscapeCsv(string.IsNullOrWhiteSpace(raw.InDate) ? raw.CreatedAtUtc.ToString("yyyy-MM-dd") : raw.InDate);
            var supplier = EscapeCsv(raw.Supplier);
            var outDate = EscapeCsv(raw.OutDate);
            var customer = EscapeCsv(raw.Customer);

            sb.AppendLine($"{tag},{serial},{model},{processor},{memory},{battHealth},{storageHealth},{status},{wip},{grade},{remarks},{tech},{inDate},{supplier},{outDate},{customer}");

            rowsPayload.Add(new Dictionary<string, object>
            {
                ["Tag"] = raw.AssetTag,
                ["Serial_Number"] = raw.SerialNumber,
                ["Model"] = raw.Model,
                ["Processor"] = ExtractProcessorNotes(raw.Notes),
                ["Memory"] = ExtractMemoryNotes(raw.Notes),
                ["Battery_Health"] = raw.BatteryHealth ?? 100,
                ["Storage_Health"] = raw.StorageHealth ?? 100,
                ["Status"] = status,
                ["Work_In_Progress"] = wip,
                ["Physical_Grade"] = grade,
                ["Remarks"] = raw.Notes,
                ["Technician"] = tech,
                ["In_Date"] = inDate,
                ["Supplier"] = raw.Supplier,
                ["Out_Date"] = raw.OutDate,
                ["Customer"] = raw.Customer,
                ["Hold_Customer"] = raw.HoldCustomer,
                ["Hold_Sales_Rep"] = raw.HoldSalesRep,
                ["Is_On_Hold"] = raw.IsOnHold
            });
        }

        // 2. Save local CSV snapshot with rotation
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var backupPath = Path.Combine(BackupDir, $"sheets_sync_{timestamp}.csv");
        await File.WriteAllTextAsync(backupPath, sb.ToString(), Encoding.UTF8, ct);

        RotateLocalBackups(15);

        // 3. Dispatch to Google Apps Script Webhook if configured
        var webhookUrl = GetConfiguredWebhookUrl();
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            return new GoogleSheetsSyncResult
            {
                Success = true,
                Message = $"Local backup saved ({assets.Count} assets). Google Sheets URL not set — configure URL to sync cloud.",
                BackupFilePath = backupPath,
                ExportedCount = assets.Count
            };
        }

        try
        {
            int syncedCount = 0;
            // Dispatch rows to Google Apps Script web app
            foreach (var row in rowsPayload)
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    var jsonContent = new StringContent(JsonSerializer.Serialize(row), Encoding.UTF8, "application/json");
                    var res = await HttpClient.PostAsync(webhookUrl, jsonContent, ct);
                    if (res.IsSuccessStatusCode) syncedCount++;
                }
                catch { }
            }

            return new GoogleSheetsSyncResult
            {
                Success = true,
                Message = $"Successfully synced {syncedCount}/{assets.Count} assets to Google Sheet & preserved local backup.",
                BackupFilePath = backupPath,
                ExportedCount = syncedCount
            };
        }
        catch (Exception ex)
        {
            return new GoogleSheetsSyncResult
            {
                Success = false,
                Message = $"Google Sheet sync warning: {ex.Message}. Local CSV backup saved successfully.",
                BackupFilePath = backupPath,
                ExportedCount = assets.Count
            };
        }
    }

    private static string ExtractProcessorNotes(string notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return "";
        var parts = notes.Split('|');
        foreach (var p in parts)
        {
            var trimmed = p.Trim();
            if (trimmed.StartsWith("CPU:", StringComparison.OrdinalIgnoreCase))
                return trimmed.Replace("CPU:", "", StringComparison.OrdinalIgnoreCase).Trim();
            if (trimmed.StartsWith("Processor:", StringComparison.OrdinalIgnoreCase))
                return trimmed.Replace("Processor:", "", StringComparison.OrdinalIgnoreCase).Trim();
        }
        return "";
    }

    private static string ExtractMemoryNotes(string notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return "";
        var parts = notes.Split('|');
        foreach (var p in parts)
        {
            var trimmed = p.Trim();
            if (trimmed.StartsWith("RAM/SSD:", StringComparison.OrdinalIgnoreCase))
                return trimmed.Replace("RAM/SSD:", "", StringComparison.OrdinalIgnoreCase).Trim();
        }
        return "";
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('\"') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        return value;
    }

    private static void RotateLocalBackups(int maxKeep)
    {
        try
        {
            var files = Directory.GetFiles(BackupDir, "sheets_sync_*.csv")
                .Select(f => new FileInfo(f))
                .OrderByDescending(fi => fi.CreationTimeUtc)
                .ToList();

            if (files.Count > maxKeep)
            {
                foreach (var old in files.Skip(maxKeep))
                {
                    try { old.Delete(); } catch { }
                }
            }
        }
        catch { }
    }
}
