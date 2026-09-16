using System;
using System.Collections.Generic;
using SuperAutoMater.Wpf.Core;

namespace SuperManager.Services
{
    /// <summary>
    /// Computes Depot OS operational metrics, aging WIP dwell times, blocker analysis,
    /// and audit logging directly from the canonical SQLite database.
    /// </summary>
    public sealed class DepotAnalyticsService
    {
        private static readonly Lazy<DepotAnalyticsService> _instance =
            new Lazy<DepotAnalyticsService>(() => new DepotAnalyticsService());

        public static DepotAnalyticsService Instance => _instance.Value;

        private readonly QcRunStore _store;

        public DepotAnalyticsService(QcRunStore store = null)
        {
            _store = store ?? new QcRunStore();
        }

        public DepotKpiSummary GetDepotKpis()
        {
            return _store.GetDepotKpis();
        }

        public List<AgingWipUnit> GetAgingWip(int limit = 10)
        {
            return _store.GetAgingWip(limit);
        }

        public List<AuditLogRecord> GetAuditLogs(int limit = 50)
        {
            return _store.GetAuditLogs(limit);
        }
    }
}
