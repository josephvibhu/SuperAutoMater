using System;
using System.Diagnostics;
using System.Media;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SuperAutoMater.Wpf.Services
{
    public enum CalibrationPhase
    {
        Phase1_ControlledDrain,
        Phase2_RestEquilibrium,
        Phase3_FullCharge,
        Phase4_Completed
    }

    public enum DrainIntensity
    {
        Light,      // Idle / Screen only (~8W - 12W)
        Standard,   // 35% - 40% CPU burn (~20W - 30W, Recommended)
        Aggressive  // 75% - 85% CPU burn (~35W - 55W)
    }

    public class BatteryCalibrationStatus
    {
        public CalibrationPhase Phase { get; set; } = CalibrationPhase.Phase1_ControlledDrain;
        public int CurrentPercent { get; set; } = 100;
        public int TargetPercent { get; set; } = 6;
        public int VoltageMv { get; set; } = 11800;
        public double VoltageVolts => Math.Round(VoltageMv / 1000.0, 2);
        public int PerCellMv => SeriesCells > 0 ? (int)Math.Round((double)VoltageMv / SeriesCells) : 0;
        public double PerCellVolts => Math.Round(PerCellMv / 1000.0, 3);
        public int SeriesCells => VoltageMv <= 9000 ? 2 : (VoltageMv <= 13500 ? 3 : 4);
        public int DrainRateMw { get; set; } = 0;
        public double DrainRateWatts => Math.Round(Math.Abs(DrainRateMw) / 1000.0, 1);
        public bool IsAcConnected { get; set; } = false;
        public bool IsDraining { get; set; } = false;
        public bool TargetFloorReached { get; set; } = false;
        public int ElapsedDrainSeconds { get; set; } = 0;
        public int EstimatedSecondsToTarget { get; set; } = 0;
        public int RestRemainingSeconds { get; set; } = 1800;
        public string StatusSummary { get; set; } = "Calibration Standby";
        public string AlertNotice { get; set; } = "";
    }

    public class BatteryCalibrationService
    {
        private static readonly Lazy<BatteryCalibrationService> _instance =
            new Lazy<BatteryCalibrationService>(() => new BatteryCalibrationService());
        public static BatteryCalibrationService Instance => _instance.Value;

        private CancellationTokenSource _drainCts;
        private Thread[] _drainWorkers;
        private System.Threading.Timer _telemetryTimer;
        private int _elapsedSec = 0;
        private int _restSecRemaining = 1800;
        private int _targetPercent = 6;
        private DrainIntensity _intensity = DrainIntensity.Standard;
        private CalibrationPhase _phase = CalibrationPhase.Phase1_ControlledDrain;
        private bool _isDraining = false;
        private bool _isResting = false;
        private bool _targetReachedAlertFired = false;

        public event Action<BatteryCalibrationStatus> StatusUpdated;

        private BatteryCalibrationService() { }

        public bool IsDraining => _isDraining;
        public bool IsResting => _isResting;
        public CalibrationPhase CurrentPhase => _phase;

        public void StartControlledDrain(int targetPercent = 6, DrainIntensity intensity = DrainIntensity.Standard)
        {
            if (_isDraining) return;

            _targetPercent = Math.Max(4, Math.Min(15, targetPercent));
            _intensity = intensity;
            _phase = CalibrationPhase.Phase1_ControlledDrain;
            _isDraining = true;
            _isResting = false;
            _elapsedSec = 0;
            _targetReachedAlertFired = false;

            // Keep system awake & display on during drain session
            try
            {
                NativeMethods.SetThreadExecutionState(
                    NativeMethods.EXECUTION_STATE.ES_CONTINUOUS |
                    NativeMethods.EXECUTION_STATE.ES_SYSTEM_REQUIRED |
                    NativeMethods.EXECUTION_STATE.ES_DISPLAY_REQUIRED);
            }
            catch { }

            // Launch throttled background workload according to intensity
            _drainCts = new CancellationTokenSource();
            int coreCount = Environment.ProcessorCount;
            int activeWorkers = _intensity switch
            {
                DrainIntensity.Light => 0,
                DrainIntensity.Standard => Math.Max(1, coreCount / 2),
                DrainIntensity.Aggressive => Math.Max(1, (coreCount * 3) / 4),
                _ => 1
            };

            if (activeWorkers > 0)
            {
                _drainWorkers = new Thread[activeWorkers];
                for (int i = 0; i < activeWorkers; i++)
                {
                    _drainWorkers[i] = new Thread(() => RunWorkerLoop(_drainCts.Token, _intensity))
                    {
                        IsBackground = true,
                        Priority = ThreadPriority.Lowest
                    };
                    _drainWorkers[i].Start();
                }
            }

            // Start 1Hz Telemetry and Safety Poller
            _telemetryTimer?.Dispose();
            _telemetryTimer = new System.Threading.Timer(OnTelemetryTick, null, 0, 1000);
        }

        private void RunWorkerLoop(CancellationToken token, DrainIntensity intensity)
        {
            int workIterations = intensity == DrainIntensity.Aggressive ? 200000 : 80000;
            int sleepMs = intensity == DrainIntensity.Aggressive ? 5 : 20;

            long val = 17;
            while (!token.IsCancellationRequested)
            {
                for (int i = 0; i < workIterations; i++)
                {
                    val = (val * 31 + 17) % 1000000007;
                }
                Thread.Sleep(sleepMs);
            }
        }

        private void OnTelemetryTick(object state)
        {
            if (_isDraining)
            {
                _elapsedSec++;
            }
            else if (_isResting)
            {
                if (_restSecRemaining > 0) _restSecRemaining--;
                if (_restSecRemaining == 0)
                {
                    _isResting = false;
                    _phase = CalibrationPhase.Phase3_FullCharge;
                    try { SystemSounds.Asterisk.Play(); } catch { }
                }
            }

            var status = ComputeStatus();

            // Safety floor check: if battery percent <= target floor, stop load immediately
            if (_isDraining && status.CurrentPercent <= _targetPercent && !_targetReachedAlertFired)
            {
                _targetReachedAlertFired = true;
                StopControlledDrain();
                _phase = CalibrationPhase.Phase2_RestEquilibrium;

                // Fire distinctive warning acoustic chimes to alert technician to plug in AC
                Task.Run(() =>
                {
                    for (int i = 0; i < 3; i++)
                    {
                        try { SystemSounds.Exclamation.Play(); } catch { }
                        Thread.Sleep(600);
                    }
                });
            }

            StatusUpdated?.Invoke(status);
        }

        public void StopControlledDrain()
        {
            _isDraining = false;
            try
            {
                _drainCts?.Cancel();
                _drainCts?.Dispose();
                _drainCts = null;
            }
            catch { }

            _drainWorkers = null;

            // Restore normal sleep state
            try
            {
                NativeMethods.SetThreadExecutionState(NativeMethods.EXECUTION_STATE.ES_CONTINUOUS);
            }
            catch { }

            StatusUpdated?.Invoke(ComputeStatus());
        }

        public void StartRestPhase(int minutes = 30)
        {
            StopControlledDrain();
            _phase = CalibrationPhase.Phase2_RestEquilibrium;
            _restSecRemaining = Math.Max(60, minutes * 60);
            _isResting = true;

            if (_telemetryTimer == null)
            {
                _telemetryTimer = new System.Threading.Timer(OnTelemetryTick, null, 0, 1000);
            }
            StatusUpdated?.Invoke(ComputeStatus());
        }

        public void SetPhase(CalibrationPhase phase)
        {
            _phase = phase;
            if (phase != CalibrationPhase.Phase1_ControlledDrain)
            {
                StopControlledDrain();
            }
            if (phase != CalibrationPhase.Phase2_RestEquilibrium)
            {
                _isResting = false;
            }
            StatusUpdated?.Invoke(ComputeStatus());
        }

        public BatteryCalibrationStatus GetCurrentStatus()
        {
            return ComputeStatus();
        }

        private BatteryCalibrationStatus ComputeStatus()
        {
            var bat = HardwareDiagnosticsService.Instance.BatteryTelemetry;
            bool powerOnline = false;
            int pct = 100;

            try
            {
                var pStatus = SystemInformation.PowerStatus;
                powerOnline = (pStatus.PowerLineStatus == PowerLineStatus.Online);
                pct = (int)Math.Round(pStatus.BatteryLifePercent * 100.0);
                if (pct <= 0 || pct > 100) pct = bat?.ChargePercent ?? 100;
            }
            catch
            {
                powerOnline = bat?.PowerOnline ?? false;
                pct = bat?.ChargePercent ?? 100;
            }

            int mv = bat?.VoltageMv ?? 11800;
            int rateMw = bat?.ChargeDischargeRateMw ?? 0;
            if (rateMw == 0 && _isDraining)
            {
                rateMw = _intensity switch
                {
                    DrainIntensity.Aggressive => -36000,
                    DrainIntensity.Standard => -22000,
                    _ => -10000
                };
            }

            int neededPctDrop = Math.Max(0, pct - _targetPercent);
            int estSeconds = 0;
            if (neededPctDrop > 0 && Math.Abs(rateMw) > 1000)
            {
                long fullMwh = bat?.FullChargeCapacityMwh ?? 45000;
                double mwhToDrain = (fullMwh * neededPctDrop) / 100.0;
                double hours = mwhToDrain / Math.Abs(rateMw);
                estSeconds = (int)Math.Round(hours * 3600.0);
            }

            var res = new BatteryCalibrationStatus
            {
                Phase = _phase,
                CurrentPercent = pct,
                TargetPercent = _targetPercent,
                VoltageMv = mv,
                DrainRateMw = rateMw,
                IsAcConnected = powerOnline,
                IsDraining = _isDraining,
                TargetFloorReached = pct <= _targetPercent,
                ElapsedDrainSeconds = _elapsedSec,
                EstimatedSecondsToTarget = estSeconds,
                RestRemainingSeconds = _restSecRemaining
            };

            if (_phase == CalibrationPhase.Phase1_ControlledDrain)
            {
                if (powerOnline && _isDraining)
                {
                    res.StatusSummary = "⚠ AC CHARGER DETECTED — UNPLUG CHARGER TO DRAIN";
                    res.AlertNotice = "Laptop is plugged in. Battery cannot discharge while AC adapter is connected.";
                }
                else if (_isDraining)
                {
                    res.StatusSummary = $"⚡ CONTROLLED DRAIN ACTIVE · {res.DrainRateWatts:F1}W LOAD";
                    res.AlertNotice = $"Discharging to {_targetPercent}% BMS floor. Auto-cutoff and alert armed.";
                }
                else if (res.TargetFloorReached)
                {
                    res.StatusSummary = $"✓ TARGET FLOOR REACHED ({pct}%) · PROCEED TO REST PHASE";
                    res.AlertNotice = "Discharge completed nominal. Connect AC soon to prevent hard shutdown.";
                }
                else
                {
                    res.StatusSummary = "Ready for Controlled Discharge Protocol";
                    res.AlertNotice = "Select drain load and click Start. Unplug AC adapter when prompted.";
                }
            }
            else if (_phase == CalibrationPhase.Phase2_RestEquilibrium)
            {
                res.StatusSummary = $"⏳ CHEMICAL RELAXATION REST ({_restSecRemaining / 60}m {_restSecRemaining % 60}s Remaining)";
                res.AlertNotice = "Allowing lithium-ion electrode polarization and cell chemistry to stabilize.";
            }
            else if (_phase == CalibrationPhase.Phase3_FullCharge)
            {
                res.StatusSummary = "🔌 CONNECT OEM CHARGER · RECHARGE UNINTERRUPTED TO 100%";
                res.AlertNotice = "Plug in AC adapter. Keep charging until 100% + 2 hours saturation to set Qmax register.";
            }
            else
            {
                res.StatusSummary = "✓ BMS RECALIBRATION COMPLETE · GAS-GAUGE RESTORED";
                res.AlertNotice = "Fuel-gauge Chemical Full Charge Capacity (Qmax) successfully relearned.";
            }

            return res;
        }

        public string GenerateProtocolSopText()
        {
            var bat = HardwareDiagnosticsService.Instance.BatteryTelemetry;
            var sb = new StringBuilder();
            sb.AppendLine("==========================================================================");
            sb.AppendLine("    SUPERAUTOMATER BMS BATTERY RECALIBRATION STANDARD OPERATING PROCEDURE ");
            sb.AppendLine("==========================================================================");
            sb.AppendLine($"Date Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Battery Model : {bat.BatteryId} · Manufacturer: {bat.Manufacturer}");
            sb.AppendLine($"Design Cap    : {bat.DesignCapacityMwh} mWh | Full Charge: {bat.FullChargeCapacityMwh} mWh");
            sb.AppendLine($"Cell Topology : {bat.CellTopology} | Voltage: {bat.VoltageVolts:F2} V");
            sb.AppendLine("--------------------------------------------------------------------------");
            sb.AppendLine("PURPOSE & SCIENTIFIC PRINCIPLE:");
            sb.AppendLine("Laptop battery management systems (TI bq-series, Renesas) calculate charge");
            sb.AppendLine("via Coulomb counting and cell impedance tracking. When batteries sit idle,");
            sb.AppendLine("gas-gauge registers drift, leading to premature shutdowns or inaccurate %.");
            sb.AppendLine("This 3-phase calibration cycle resets Qmax (Full Charge Capacity).");
            sb.AppendLine("--------------------------------------------------------------------------");
            sb.AppendLine("PHASE 1: CONTROLLED DEEP DISCHARGE (Floor: 5% - 7%)");
            sb.AppendLine("  1. Disconnect the OEM AC adapter.");
            sb.AppendLine("  2. In SuperAutoMater, start Controlled Drain Mode under 35-40% load.");
            sb.AppendLine("  3. Allow system to discharge smoothly until reaching the 6% safety floor.");
            sb.AppendLine("  4. SuperAutoMater will emit an acoustic alarm to prevent brownout shutoff.");
            sb.AppendLine("--------------------------------------------------------------------------");
            sb.AppendLine("PHASE 2: CHEMICAL & THERMAL RELAXATION (30 - 60 Minutes)");
            sb.AppendLine("  1. Leave the laptop powered on idle or shut down in standby.");
            sb.AppendLine("  2. Allow internal lithium-ion concentration gradients to reach equilibrium.");
            sb.AppendLine("  3. Ensures cell core temperature drops to ambient (25°C).");
            sb.AppendLine("--------------------------------------------------------------------------");
            sb.AppendLine("PHASE 3: CONTINUOUS UNINTERRUPTED RECHARGE (100% + 2 Hours Saturation)");
            sb.AppendLine("  1. Plug in genuine OEM AC power adapter.");
            sb.AppendLine("  2. Charge continuously to 100% without unplugging.");
            sb.AppendLine("  3. Maintain AC connected for 2 additional hours after 100% indicator.");
            sb.AppendLine("  4. BMS registers current taper point (Itaper) and commits new Qmax register.");
            sb.AppendLine("==========================================================================");
            return sb.ToString();
        }
    }
}
