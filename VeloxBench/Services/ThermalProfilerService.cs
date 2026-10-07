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

        // Cool-down recovery phase metrics
        public bool IsCoolDownActive { get; set; } = false;
        public int CoolDownTempC { get; set; } = 40;
        public double CoolDownSeconds { get; set; } = 0.0;
        public int DissipationDeltaT => Math.Max(0, PeakTempC - CoolDownTempC);
        public double CoolDownRatePerSec => CoolDownSeconds > 0.5 ? Math.Round(DissipationDeltaT / CoolDownSeconds, 2) : 0.0;

        // Radiator & Exhaust Fin Cool-Down Decay Metrics
        public double DecayHalfLifeSeconds { get; set; } = 0.0;
        public string RadiatorAirflowVerdict { get; set; } = "Thermal Airflow Standby";
        public string RadiatorAirflowBadge { get; set; } = "STANDBY";
        public string RadiatorAirflowAccentHex { get; set; } = "#8B949E";

        public int MaxThrottlingPercent { get; set; } = 0;
        public bool IsThrottling => MaxThrottlingPercent > 5;
        public string ConditionCode { get; set; } = "IDLE";
        public string ConditionSummary { get; set; } = "Thermal Profiler Standby";
        public string DissipationVerdictBadge { get; set; } = "STANDBY";
        public string AccentHex { get; set; } = "#8B949E";
    }

    public class ThermalProfilerService
    {
        private static readonly Lazy<ThermalProfilerService> _instance =
            new Lazy<ThermalProfilerService>(() => new ThermalProfilerService());
        public static ThermalProfilerService Instance => _instance.Value;

        private bool _isProfiling = false;
        private bool _isCoolDown = false;
        private DateTime _startTime;
        private DateTime _coolDownStartTime;
        private int _baselineTemp = 40;
        private int _peakTemp = 40;
        private int _coolDownTemp = 40;
        private int _coolDownPeakTemp = 40;
        private int _targetHalfDecayTemp = 40;
        private double _halfLifeAchievedSeconds = -1.0;
        private int _maxThrottling = 0;
        private double _stressDurationSeconds = 0.0;
        private double _coolDownDurationSeconds = 0.0;
        private readonly List<(double Sec, int Temp)> _samples = new List<(double, int)>();

        public event Action<ThermalProfileResult> ProfileUpdated;

        private ThermalProfilerService() { }

        public void StartProfiling(int initialTempC)
        {
            _baselineTemp = initialTempC > 0 ? initialTempC : 42;
            _peakTemp = _baselineTemp;
            _coolDownTemp = _baselineTemp;
            _coolDownPeakTemp = _baselineTemp;
            _targetHalfDecayTemp = _baselineTemp;
            _halfLifeAchievedSeconds = -1.0;
            _maxThrottling = 0;
            _startTime = DateTime.Now;
            _stressDurationSeconds = 0.0;
            _coolDownDurationSeconds = 0.0;
            _samples.Clear();
            _samples.Add((0.0, _baselineTemp));
            _isProfiling = true;
            _isCoolDown = false;

            NotifyUpdate();
        }

        public void UpdateSample(int currentTempC, int throttlingPercent = 0)
        {
            if (!_isProfiling || _isCoolDown) return;

            double elapsed = (DateTime.Now - _startTime).TotalSeconds;
            _stressDurationSeconds = elapsed;
            if (currentTempC > _peakTemp) _peakTemp = currentTempC;
            if (throttlingPercent > _maxThrottling) _maxThrottling = throttlingPercent;
            _coolDownTemp = currentTempC;

            _samples.Add((elapsed, currentTempC));
            NotifyUpdate();
        }

        public void StartCoolDown(int currentTempC)
        {
            _isCoolDown = true;
            _coolDownStartTime = DateTime.Now;
            _coolDownPeakTemp = currentTempC > 0 ? Math.Max(currentTempC, _peakTemp) : _peakTemp;
            _coolDownTemp = _coolDownPeakTemp;
            _coolDownDurationSeconds = 0.0;
            _halfLifeAchievedSeconds = -1.0;

            // Target half-decay temperature: midpoint between peak and baseline
            int deltaRise = Math.Max(4, _coolDownPeakTemp - _baselineTemp);
            _targetHalfDecayTemp = _baselineTemp + (int)Math.Round(deltaRise * 0.5);

            NotifyUpdate();
        }

        public void UpdateCoolDownSample(int currentTempC, double? overrideElapsedSeconds = null)
        {
            if (!_isProfiling || !_isCoolDown) return;

            _coolDownTemp = currentTempC;
            double coolSec = overrideElapsedSeconds ?? (DateTime.Now - _coolDownStartTime).TotalSeconds;
            _coolDownDurationSeconds = coolSec;

            // Check if actual half-life temperature drop has been reached
            if (_halfLifeAchievedSeconds < 0 && currentTempC <= _targetHalfDecayTemp && (_coolDownPeakTemp - _baselineTemp >= 6))
            {
                _halfLifeAchievedSeconds = Math.Max(0.5, Math.Round(coolSec, 1));
            }

            NotifyUpdate();
        }

        public ThermalProfileResult StopProfiling()
        {
            _isProfiling = false;
            _isCoolDown = false;
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
            double stressSec = _isProfiling
                ? (_isCoolDown ? _stressDurationSeconds : (DateTime.Now - _startTime).TotalSeconds)
                : (_stressDurationSeconds > 0 ? _stressDurationSeconds : (_samples.Count > 1 ? _samples.Last().Sec : 0.0));

            double coolSec = _coolDownDurationSeconds > 0
                ? _coolDownDurationSeconds
                : (_isCoolDown ? (DateTime.Now - _coolDownStartTime).TotalSeconds : 0.0);

            var res = new ThermalProfileResult
            {
                BaselineTempC = _baselineTemp,
                PeakTempC = _peakTemp,
                ElapsedSeconds = Math.Round(stressSec, 1),
                IsCoolDownActive = _isCoolDown,
                CoolDownTempC = _coolDownTemp,
                CoolDownSeconds = Math.Round(coolSec, 1),
                MaxThrottlingPercent = _maxThrottling
            };

            // Calculate or estimate Decay Half-Life
            if (_halfLifeAchievedSeconds >= 0)
            {
                res.DecayHalfLifeSeconds = Math.Max(0.5, _halfLifeAchievedSeconds);
            }
            else if (res.CoolDownSeconds > 1.0 && res.CoolDownRatePerSec > 0.05)
            {
                double neededDrop = Math.Max(2.0, (_coolDownPeakTemp - _baselineTemp) * 0.5);
                res.DecayHalfLifeSeconds = Math.Round(neededDrop / Math.Max(0.1, res.CoolDownRatePerSec), 1);
            }
            else
            {
                res.DecayHalfLifeSeconds = 0.0;
            }

            // Radiator Airflow / Heatsink Fin Analysis
            if (_isCoolDown && res.CoolDownSeconds < 2.0 && _halfLifeAchievedSeconds < 0)
            {
                res.RadiatorAirflowBadge = "MEASURING DECAY";
                res.RadiatorAirflowVerdict = "Cool-Down: Monitoring thermal dissipation decay curve...";
                res.RadiatorAirflowAccentHex = "#58A6FF";
            }
            else if ((res.DecayHalfLifeSeconds > 0 && res.DecayHalfLifeSeconds <= 8.5) || res.CoolDownRatePerSec >= 1.8)
            {
                res.RadiatorAirflowBadge = "✓ CLEAR RADIATOR";
                string halfLifeTxt = res.DecayHalfLifeSeconds > 0 ? $" · {res.DecayHalfLifeSeconds:F1}s Half-Life" : "";
                res.RadiatorAirflowVerdict = $"✓ RAPID DECAY (-{res.CoolDownRatePerSec:F1}°C/s{halfLifeTxt} · Radiator Fins Clear)";
                res.RadiatorAirflowAccentHex = "#3FB950";
            }
            else if ((res.CoolDownSeconds >= 4.0 && (res.DecayHalfLifeSeconds > 18.0 || res.CoolDownRatePerSec < 0.7)) && res.PeakTempC >= 75)
            {
                res.RadiatorAirflowBadge = "🚨 HEATSINK RESTRICTED";
                string halfLifeTxt = res.DecayHalfLifeSeconds > 0 ? $" · {res.DecayHalfLifeSeconds:F1}s Half-Life" : "";
                res.RadiatorAirflowVerdict = $"🚨 RESTRICTED AIRFLOW / CLOGGED EXHAUST FINS (-{res.CoolDownRatePerSec:F1}°C/s Slow Decay{halfLifeTxt})";
                res.RadiatorAirflowAccentHex = "#F85149";
            }
            else if (res.CoolDownSeconds >= 2.0 || _halfLifeAchievedSeconds >= 0 || res.DissipationDeltaT >= 8)
            {
                res.RadiatorAirflowBadge = "✓ AIRFLOW NOMINAL";
                string halfLifeTxt = res.DecayHalfLifeSeconds > 0 ? $" · {res.DecayHalfLifeSeconds:F1}s Half-Life" : "";
                res.RadiatorAirflowVerdict = $"✓ AIRFLOW NOMINAL (-{res.CoolDownRatePerSec:F1}°C/s{halfLifeTxt})";
                res.RadiatorAirflowAccentHex = "#3FB950";
            }
            else
            {
                res.RadiatorAirflowBadge = "STANDBY";
                res.RadiatorAirflowVerdict = "Awaiting CPU Burn Cool-Down...";
                res.RadiatorAirflowAccentHex = "#8B949E";
            }

            // Overall Thermal Paste / Conduction Verdict
            if (_isProfiling && res.ElapsedSeconds < 2.0 && !_isCoolDown && res.PeakTempC < 95 && res.MaxThrottlingPercent < 15)
            {
                res.ConditionCode = "STRESS_STARTING";
                res.ConditionSummary = $"Baseline {res.BaselineTempC}°C · Measuring Rise Rate...";
                res.DissipationVerdictBadge = "MEASURING RISE";
                res.AccentHex = "#58A6FF";
            }
            else if (_isProfiling && _isCoolDown && res.CoolDownSeconds < 3.0)
            {
                res.ConditionCode = "COOL_DOWN";
                res.ConditionSummary = $"Cool-Down: {res.CoolDownTempC}°C (Peak {res.PeakTempC}°C · Dissipation -{res.CoolDownRatePerSec}°C/s)";
                res.DissipationVerdictBadge = "COOLING RECOVERY";
                res.AccentHex = "#58A6FF";
            }
            else if (res.PeakTempC >= 95 || (res.DeltaT >= 42 && res.ElapsedSeconds <= 12.0) || res.RiseRatePerSec >= 4.5 || res.MaxThrottlingPercent >= 15)
            {
                res.ConditionCode = "DEGRADED_PASTE";
                res.ConditionSummary = $"🚨 THERMAL PASTE DEGRADED — SERVICE RECOMMENDED (+{res.RiseRatePerSec}°C/s Rise · Peak {res.PeakTempC}°C)";
                res.DissipationVerdictBadge = "⚠ RE-PASTE RECOMMENDED";
                res.AccentHex = "#F85149";
            }
            else if (res.CoolDownSeconds >= 4.0 && res.CoolDownRatePerSec < 0.6 && res.PeakTempC >= 80)
            {
                res.ConditionCode = "HEATSINK_RESTRICTED";
                res.ConditionSummary = $"⚠ RESTRICTED AIRFLOW / FAN CLOGGED (-{res.CoolDownRatePerSec}°C/s Slow Dissipation · Peak {res.PeakTempC}°C)";
                res.DissipationVerdictBadge = "⚠ AIRFLOW RESTRICTED";
                res.AccentHex = "#D29922";
            }
            else if (res.PeakTempC >= 85 || res.RiseRatePerSec >= 3.0)
            {
                res.ConditionCode = "ELEVATED";
                res.ConditionSummary = $"Moderate Thermal Rise (+{res.RiseRatePerSec}°C/s Rise · Peak {res.PeakTempC}°C · Fans Responding)";
                res.DissipationVerdictBadge = "MODERATE RISE";
                res.AccentHex = "#D29922";
            }
            else
            {
                res.ConditionCode = "NOMINAL";
                string coolSuffix = res.CoolDownRatePerSec > 0 ? $" · -{res.CoolDownRatePerSec}°C/s Dissipation" : "";
                res.ConditionSummary = $"✓ HEATSINK & THERMAL DISSIPATION NOMINAL (+{res.RiseRatePerSec}°C/s Rise{coolSuffix} · Peak {res.PeakTempC}°C)";
                res.DissipationVerdictBadge = "✓ DISSIPATION NOMINAL";
                res.AccentHex = "#3FB950";
            }

            return res;
        }
    }
}
