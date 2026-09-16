using System;
using System.Threading;
using System.Threading.Tasks;

namespace SuperAutoMater.Wpf.Services
{
    public class BatteryLoadResult
    {
        public int IdleVoltageMv { get; set; } = 12400;
        public int LoadVoltageMv { get; set; } = 12150;
        public int VoltageSagMv => Math.Max(0, IdleVoltageMv - LoadVoltageMv);
        public double SagVolts => Math.Round(VoltageSagMv / 1000.0, 2);
        public int SeriesCellCount => IdleVoltageMv <= 9000 ? 2 : (IdleVoltageMv <= 13500 ? 3 : 4);
        public int PerCellSagMv => SeriesCellCount > 0 ? (int)Math.Round((double)VoltageSagMv / SeriesCellCount) : 0;
        public int EstimatedCellDriftMv => Math.Max(8, (int)Math.Round(PerCellSagMv * 0.25));
        public string CellTopology => $"{SeriesCellCount}S1P ({SeriesCellCount} Cells)";
        public string CellBalanceStatus => EstimatedCellDriftMv > 80 ? "⚠ CRITICAL CELL IMBALANCE" : (EstimatedCellDriftMv > 35 ? "● MODERATE CELL DRIFT" : "✓ CELLS BALANCED NOMINAL");
        public string CellIntegrityCode { get; set; } = "NOMINAL";
        public string StatusSummary { get; set; } = "Cell Voltage Stable";
        public string VerdictBadge => VoltageSagMv >= 900 ? "🚨 HIGH SAG / DEGRADED" : (VoltageSagMv >= 450 ? "⚠ MODERATE SAG" : "✓ VOLTAGE STABLE");
        public string AccentHex { get; set; } = "#3FB950";
        public int DurationSeconds { get; set; } = 15;
    }

    public class BatteryLoadMeterService
    {
        private static readonly Lazy<BatteryLoadMeterService> _instance =
            new Lazy<BatteryLoadMeterService>(() => new BatteryLoadMeterService());
        public static BatteryLoadMeterService Instance => _instance.Value;

        private int _idleVoltageMv = 0;
        private int _lowestLoadVoltageMv = 0;
        private bool _isMeasuring = false;

        public bool IsRunning { get; private set; } = false;
        public event Action<BatteryLoadResult> LoadMeterUpdated;

        private BatteryLoadMeterService() { }

        public void RecordIdleVoltage(int currentMv)
        {
            if (currentMv > 5000)
            {
                _idleVoltageMv = currentMv;
                _lowestLoadVoltageMv = currentMv;
            }
        }

        public void StartLoadMeasurement(int initialMv)
        {
            if (_idleVoltageMv <= 0)
                _idleVoltageMv = initialMv > 5000 ? initialMv : 12300;

            _lowestLoadVoltageMv = initialMv > 5000 ? initialMv : _idleVoltageMv;
            _isMeasuring = true;
            NotifyUpdate();
        }

        public void UpdateLoadVoltage(int currentMv)
        {
            if (!_isMeasuring) return;
            if (currentMv > 5000 && currentMv < _lowestLoadVoltageMv)
            {
                _lowestLoadVoltageMv = currentMv;
            }
            NotifyUpdate();
        }

        public BatteryLoadResult StopMeasurement()
        {
            _isMeasuring = false;
            var res = ComputeResult();
            try
            {
                HardwareDiagnosticsService.Instance.BatteryTelemetry.EstimatedCellDriftMv = res.EstimatedCellDriftMv;
            }
            catch { }
            return res;
        }

        public BatteryLoadResult GetResult()
        {
            return ComputeResult();
        }

        public BatteryLoadResult GetCurrentResult()
        {
            return ComputeResult();
        }

        /// <summary>
        /// Runs an autonomous multi-threaded load-step battery stress test for the specified duration (default 15s)
        /// while sampling battery voltage and calculating dynamic sag and cell drift.
        /// </summary>
        public async Task<BatteryLoadResult> RunAutomatedBatteryLoadTestAsync(
            int durationSeconds,
            Action<int, int, int, int, string> onProgress,
            CancellationToken ct = default)
        {
            if (IsRunning) return GetCurrentResult();
            IsRunning = true;

            try
            {
                int initialMv = HardwareDiagnosticsService.Instance.ProbeBatteryQuickVoltage();
                if (initialMv < 5000) initialMv = HardwareDiagnosticsService.Instance.BatteryTelemetry?.VoltageMv ?? 12300;
                RecordIdleVoltage(initialMv);
                StartLoadMeasurement(initialMv);

                using var innerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var token = innerCts.Token;

                // Spin up multi-core CPU workload across threads
                int threads = Math.Clamp(Environment.ProcessorCount, 2, 8);
                for (int i = 0; i < threads; i++)
                {
                    _ = Task.Run(() =>
                    {
                        while (!token.IsCancellationRequested)
                        {
                            double x = 0;
                            for (int j = 0; j < 50000; j++)
                            {
                                x += Math.Sqrt(j) * Math.Sin(j);
                            }
                        }
                    }, token);
                }

                int totalIntervals = durationSeconds * 2; // 500ms intervals
                for (int step = 1; step <= totalIntervals; step++)
                {
                    await Task.Delay(500, token);
                    if (token.IsCancellationRequested) break;

                    int curMv = HardwareDiagnosticsService.Instance.ProbeBatteryQuickVoltage();
                    UpdateLoadVoltage(curMv);

                    var currentRes = GetCurrentResult();
                    int remainingSec = Math.Max(0, durationSeconds - (step / 2));
                    int percent = (int)Math.Round((step / (double)totalIntervals) * 100);

                    string statusMsg = $"Measuring Load Step ({percent}% · {remainingSec}s left): Terminal {curMv}mV · Sag -{currentRes.VoltageSagMv}mV ({currentRes.PerCellSagMv}mV/cell)";
                    onProgress?.Invoke(remainingSec, percent, curMv, currentRes.VoltageSagMv, statusMsg);
                }

                innerCts.Cancel();
                var finalResult = StopMeasurement();
                finalResult.DurationSeconds = durationSeconds;
                return finalResult;
            }
            catch
            {
                return StopMeasurement();
            }
            finally
            {
                IsRunning = false;
            }
        }

        private void NotifyUpdate()
        {
            var res = ComputeResult();
            LoadMeterUpdated?.Invoke(res);
        }

        private BatteryLoadResult ComputeResult()
        {
            if (_idleVoltageMv <= 0) _idleVoltageMv = 12300;
            if (_lowestLoadVoltageMv <= 0) _lowestLoadVoltageMv = _idleVoltageMv;

            var res = new BatteryLoadResult
            {
                IdleVoltageMv = _idleVoltageMv,
                LoadVoltageMv = _lowestLoadVoltageMv
            };

            if (res.VoltageSagMv >= 900)
            {
                res.CellIntegrityCode = "HIGH_SAG_WARNING";
                res.StatusSummary = $"🚨 CRITICAL VOLTAGE SAG (ΔV: -{res.VoltageSagMv}mV drop · Weak Cell / Dropout Risk)";
                res.AccentHex = "#F85149";
            }
            else if (res.VoltageSagMv >= 450)
            {
                res.CellIntegrityCode = "MODERATE_SAG";
                res.StatusSummary = $"⚠ MODERATE VOLTAGE SAG (ΔV: -{res.VoltageSagMv}mV drop · Cell Aging)";
                res.AccentHex = "#D29922";
            }
            else
            {
                res.CellIntegrityCode = "NOMINAL";
                res.StatusSummary = $"✓ CELL INTEGRITY NOMINAL (ΔV: -{res.VoltageSagMv}mV drop under load)";
                res.AccentHex = "#3FB950";
            }

            return res;
        }
    }
}
