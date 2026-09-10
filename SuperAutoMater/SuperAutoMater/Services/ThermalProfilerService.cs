using System;
using System.Collections.Generic;
using System.Linq;

namespace SuperAutoMater.Wpf.Services
{
    public class ThermalProfileResult
    {
        public int BaselineTempC { get; set; } = 40;
        public int PeakTempC { get; set; } = 40;
        public int DeltaT => Math.Max(0, PeakTempC - BaselineTempC);
        public double ElapsedSeconds { get; set; } = 0.0;
        public double RiseRatePerSec => ElapsedSeconds > 0.5 ? Math.Round(DeltaT / ElapsedSeconds, 2) : 0.0;
        public int MaxThrottlingPercent { get; set; } = 0;
        public bool IsThrottling => MaxThrottlingPercent > 5;
        public string ConditionCode { get; set; } = "IDLE";
        public string ConditionSummary { get; set; } = "Thermal Profiler Standby";
        public string AccentHex { get; set; } = "#8B949E";
    }

    public class ThermalProfilerService
    {
        private static readonly Lazy<ThermalProfilerService> _instance =
            new Lazy<ThermalProfilerService>(() => new ThermalProfilerService());
        public static ThermalProfilerService Instance => _instance.Value;

        private bool _isProfiling = false;
        private DateTime _startTime;
        private int _baselineTemp = 40;
        private int _peakTemp = 40;
        private int _maxThrottling = 0;
        private readonly List<(double Sec, int Temp)> _samples = new List<(double, int)>();

        public event Action<ThermalProfileResult> ProfileUpdated;

        private ThermalProfilerService() { }

        public void StartProfiling(int initialTempC)
        {
            _baselineTemp = initialTempC > 0 ? initialTempC : 42;
            _peakTemp = _baselineTemp;
            _maxThrottling = 0;
            _startTime = DateTime.Now;
            _samples.Clear();
            _samples.Add((0.0, _baselineTemp));
            _isProfiling = true;

            NotifyUpdate();
        }

        public void UpdateSample(int currentTempC, int throttlingPercent = 0)
        {
            if (!_isProfiling) return;

            double elapsed = (DateTime.Now - _startTime).TotalSeconds;
            if (currentTempC > _peakTemp) _peakTemp = currentTempC;
            if (throttlingPercent > _maxThrottling) _maxThrottling = throttlingPercent;

            _samples.Add((elapsed, currentTempC));
            NotifyUpdate();
        }

        public ThermalProfileResult StopProfiling()
        {
            _isProfiling = false;
            return ComputeResult();
        }

        public ThermalProfileResult GetCurrentResult()
        {
            return ComputeResult();
        }

        private void NotifyUpdate()
        {
            var res = ComputeResult();
            ProfileUpdated?.Invoke(res);
        }

        private ThermalProfileResult ComputeResult()
        {
            double elapsed = _isProfiling ? (DateTime.Now - _startTime).TotalSeconds : (_samples.Count > 1 ? _samples.Last().Sec : 0.0);
            var res = new ThermalProfileResult
            {
                BaselineTempC = _baselineTemp,
                PeakTempC = _peakTemp,
                ElapsedSeconds = Math.Round(elapsed, 1),
                MaxThrottlingPercent = _maxThrottling
            };

            if (res.ElapsedSeconds < 2.0)
            {
                res.ConditionCode = "STRESS_STARTING";
                res.ConditionSummary = $"Baseline {res.BaselineTempC}°C · Measuring Rise Rate...";
                res.AccentHex = "#58A6FF";
            }
            else if (res.PeakTempC >= 95 || (res.DeltaT >= 42 && res.ElapsedSeconds <= 12.0) || res.MaxThrottlingPercent >= 15)
            {
                res.ConditionCode = "DEGRADED_PASTE";
                res.ConditionSummary = $"⚠ THERMAL PASTE DEGRADED — SERVICE RECOMMENDED (+{res.RiseRatePerSec}°C/s · Peak {res.PeakTempC}°C)";
                res.AccentHex = "#F85149";
            }
            else if (res.PeakTempC >= 85 || res.RiseRatePerSec >= 2.8)
            {
                res.ConditionCode = "ELEVATED";
                res.ConditionSummary = $"Moderate Thermal Rise (+{res.RiseRatePerSec}°C/s · Peak {res.PeakTempC}°C · Fans Working)";
                res.AccentHex = "#D29922";
            }
            else
            {
                res.ConditionCode = "NOMINAL";
                res.ConditionSummary = $"✓ HEATSINK & THERMAL CONDUCTION NOMINAL (+{res.RiseRatePerSec}°C/s · Peak {res.PeakTempC}°C)";
                res.AccentHex = "#3FB950";
            }

            return res;
        }
    }
}
