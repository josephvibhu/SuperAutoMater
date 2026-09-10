using System;
using System.IO;
using System.Threading.Tasks;
using NAudio.Wave;

namespace SuperAutoMater.Wpf.Services
{
    public class AcousticTestResult
    {
        public bool IsCouplingVerified { get; set; } = false;
        public float MaxInputLevel { get; set; } = 0f;
        public string StatusSummary { get; set; } = "Awaiting Acoustic Sweep";
        public string AccentHex { get; set; } = "#8B949E";
    }

    public class AcousticAnalyzerService
    {
        private static readonly Lazy<AcousticAnalyzerService> _instance =
            new Lazy<AcousticAnalyzerService>(() => new AcousticAnalyzerService());
        public static AcousticAnalyzerService Instance => _instance.Value;

        private WaveInEvent _waveIn;
        private float _currentPeak = 0f;
        private bool _isRecording = false;

        private AcousticAnalyzerService() { }

        public void StartListening()
        {
            try
            {
                if (_isRecording) StopListening();

                if (WaveInEvent.DeviceCount > 0)
                {
                    _currentPeak = 0f;
                    _waveIn = new WaveInEvent
                    {
                        DeviceNumber = 0,
                        WaveFormat = new WaveFormat(44100, 1),
                        BufferMilliseconds = 50
                    };

                    _waveIn.DataAvailable += (s, e) =>
                    {
                        float max = 0;
                        for (int index = 0; index < e.BytesRecorded; index += 2)
                        {
                            short sample = (short)((e.Buffer[index + 1] << 8) | e.Buffer[index]);
                            var sample32 = sample / 32768f;
                            if (sample32 < 0) sample32 = -sample32;
                            if (sample32 > max) max = sample32;
                        }

                        if (max > _currentPeak)
                            _currentPeak = max;
                    };

                    _waveIn.StartRecording();
                    _isRecording = true;
                }
            }
            catch { }
        }

        public AcousticTestResult StopAndAnalyze()
        {
            float peak = _currentPeak;
            StopListening();

            var res = new AcousticTestResult
            {
                MaxInputLevel = peak
            };

            // If microphone picked up speaker sweep audio (> 4% amplitude threshold)
            if (peak >= 0.04f)
            {
                res.IsCouplingVerified = true;
                res.StatusSummary = $"✓ ACOUSTIC COUPLING NOMINAL · Transduction Level: {(int)(peak * 100)}% Verified";
                res.AccentHex = "#3FB950";
            }
            else
            {
                res.IsCouplingVerified = false;
                res.StatusSummary = "Audio sweep completed · Transduction below threshold (Check Mic / Volume)";
                res.AccentHex = "#D29922";
            }

            return res;
        }

        public void StopListening()
        {
            try
            {
                if (_isRecording && _waveIn != null)
                {
                    _isRecording = false;
                    _waveIn.StopRecording();
                    _waveIn.Dispose();
                    _waveIn = null;
                }
            }
            catch { }
        }
    }
}
