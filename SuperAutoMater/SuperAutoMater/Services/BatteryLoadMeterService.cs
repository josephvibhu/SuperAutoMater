using System;

namespace SuperAutoMater.Wpf.Services
{
    public class BatteryLoadResult
    {
        public int IdleVoltageMv { get; set; } = 12400;
        public int LoadVoltageMv { get; set; } = 12150;
        public int VoltageSagMv => Math.Max(0, IdleVoltageMv - LoadVoltageMv);
        public double SagVolts => Math.Round(VoltageSagMv / 1000.0, 2);
        public string CellIntegrityCode { get; set; } = "NOMINAL";
        public string StatusSummary { get; set; } = "Cell Voltage Stable";
        public string AccentHex { get; set; } = "#3FB950";
    }

    public class BatteryLoadMeterService
    {
        private static readonly Lazy<BatteryLoadMeterService> _instance =
            new Lazy<BatteryLoadMeterService>(() => new BatteryLoadMeterService());
        public static BatteryLoadMeterService Instance => _instance.Value;

        private int _idleVoltageMv = 0;
        private int _lowestLoadVoltageMv = 0;
        private bool _isMeasuring = false;

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
            return ComputeResult();
        }

        public BatteryLoadResult GetResult()
        {
            return ComputeResult();
        }

        public BatteryLoadResult GetCurrentResult()
        {
            return ComputeResult();
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

            if (res.VoltageSagMv >= 1200)
            {
                res.CellIntegrityCode = "HIGH_SAG_WARNING";
                res.StatusSummary = $"⚠ HIGH INTERNAL RESISTANCE (ΔV: {res.SagVolts}V drop · Sudden Dropout Risk)";
                res.AccentHex = "#F85149";
            }
            else if (res.VoltageSagMv >= 500)
            {
                res.CellIntegrityCode = "MODERATE_SAG";
                res.StatusSummary = $"Moderate Cell Sag (ΔV: {res.SagVolts}V drop · Aging Cells)";
                res.AccentHex = "#D29922";
            }
            else
            {
                res.CellIntegrityCode = "NOMINAL";
                res.StatusSummary = $"✓ CELL INTEGRITY NOMINAL (ΔV: {res.SagVolts}V drop under load)";
                res.AccentHex = "#3FB950";
            }

            return res;
        }
    }
}
