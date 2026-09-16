using System;

namespace SuperManager.Models
{
    public class FleetKpiSummary
    {
        public int TotalActiveBenches { get; set; } = 0;
        public int TotalTesting { get; set; } = 0;
        public int TotalCompletedToday { get; set; } = 0;
        public int TotalAlerts { get; set; } = 0;
        public double FirstTimePassRate { get; set; } = 100.0;
        public string GradeDistribution { get; set; } = "A+: 0 | A: 0 | B: 0";
    }

    public class InventoryRecord
    {
        public string SerialNumber { get; set; } = "";
        public string MachineName { get; set; } = "";
        public string Manufacturer { get; set; } = "";
        public string Model { get; set; } = "";
        public string Grade { get; set; } = "GRADE A+";
        public string Cpu { get; set; } = "";
        public string Ram { get; set; } = "";
        public string Storage { get; set; } = "";
        public string BatteryHealth { get; set; } = "100%";
        public int PassedCount { get; set; } = 0;
        public int TotalCount { get; set; } = 11;
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public string CertificatePath { get; set; } = "";

        public string FormattedTimestamp => TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    }
}
