using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using SuperManager.Models;

namespace SuperManager.Services
{
    public class InventoryStorageService
    {
        private static readonly Lazy<InventoryStorageService> _instance =
            new Lazy<InventoryStorageService>(() => new InventoryStorageService());
        public static InventoryStorageService Instance => _instance.Value;

        private readonly string _dbPath;
        private readonly List<InventoryRecord> _records = new List<InventoryRecord>();
        private readonly object _lock = new object();

        private InventoryStorageService()
        {
            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SuperAutoMater", "Manager");
            Directory.CreateDirectory(appData);
            _dbPath = Path.Combine(appData, "inventory_fleet.json");
            LoadDatabase();
        }

        private void LoadDatabase()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(_dbPath))
                    {
                        string json = File.ReadAllText(_dbPath);
                        var list = JsonSerializer.Deserialize<List<InventoryRecord>>(json);
                        if (list != null)
                        {
                            _records.Clear();
                            _records.AddRange(list);
                        }
                    }
                }
                catch { }
            }
        }

        public void SaveRecord(InventoryRecord record)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.SerialNumber)) return;
            lock (_lock)
            {
                // Replace or append
                int existingIdx = _records.FindIndex(r => r.SerialNumber.Equals(record.SerialNumber, StringComparison.OrdinalIgnoreCase));
                if (existingIdx >= 0)
                {
                    _records[existingIdx] = record;
                }
                else
                {
                    _records.Add(record);
                }

                try
                {
                    string json = JsonSerializer.Serialize(_records, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_dbPath, json);
                }
                catch { }
            }
        }

        public IReadOnlyList<InventoryRecord> GetAllRecords()
        {
            lock (_lock)
            {
                return _records.OrderByDescending(r => r.TimestampUtc).ToList();
            }
        }

        public string ExportToCsv(string targetFilePath)
        {
            lock (_lock)
            {
                var sb = new StringBuilder();
                sb.AppendLine("Timestamp,Serial,MachineName,Manufacturer,Model,Grade,Passed,Total,BatteryHealth,Storage,CPU,RAM,Certificate");

                foreach (var r in _records.OrderByDescending(r => r.TimestampUtc))
                {
                    sb.AppendLine($"\"{r.FormattedTimestamp}\",\"{EscapeCsv(r.SerialNumber)}\",\"{EscapeCsv(r.MachineName)}\",\"{EscapeCsv(r.Manufacturer)}\",\"{EscapeCsv(r.Model)}\",\"{EscapeCsv(r.Grade)}\",{r.PassedCount},{r.TotalCount},\"{EscapeCsv(r.BatteryHealth)}\",\"{EscapeCsv(r.Storage)}\",\"{EscapeCsv(r.Cpu)}\",\"{EscapeCsv(r.Ram)}\",\"{EscapeCsv(r.CertificatePath)}\"");
                }

                File.WriteAllText(targetFilePath, sb.ToString(), Encoding.UTF8);
                return targetFilePath;
            }
        }

        private static string EscapeCsv(string val)
        {
            if (string.IsNullOrEmpty(val)) return "";
            return val.Replace("\"", "\"\"");
        }

        public FleetKpiSummary ComputeKpiSummary(IEnumerable<BenchDevice> activeBenches)
        {
            var summary = new FleetKpiSummary();
            var activeList = activeBenches?.ToList() ?? new List<BenchDevice>();

            summary.TotalActiveBenches = activeList.Count(b => b.IsOnline);
            summary.TotalTesting = activeList.Count(b => b.IsOnline && b.PassedCount > 0 && b.PassedCount < b.TotalCount);
            summary.TotalAlerts = activeList.Count(b => b.Alert || b.CpuTemp >= 90);

            lock (_lock)
            {
                var today = DateTime.UtcNow.Date;
                var todayRecords = _records.Where(r => r.TimestampUtc.Date == today).ToList();
                summary.TotalCompletedToday = todayRecords.Count;

                if (todayRecords.Count > 0)
                {
                    int passedFirstTime = todayRecords.Count(r => r.PassedCount >= r.TotalCount);
                    summary.FirstTimePassRate = Math.Round((double)passedFirstTime / todayRecords.Count * 100.0, 1);

                    int aPlus = todayRecords.Count(r => r.Grade.Contains("A+"));
                    int a = todayRecords.Count(r => r.Grade.Contains("A") && !r.Grade.Contains("A+"));
                    int b = todayRecords.Count(r => r.Grade.Contains("B"));
                    summary.GradeDistribution = $"A+: {aPlus} | A: {a} | B: {b}";
                }
                else
                {
                    summary.FirstTimePassRate = 100.0;
                    summary.GradeDistribution = "A+: 0 | A: 0 | B: 0";
                }
            }

            return summary;
        }
    }
}
