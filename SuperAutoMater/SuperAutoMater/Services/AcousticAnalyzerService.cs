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
        public float TotalHarmonicDistortionPercent { get; set; } = 1.0f;
        public bool IsBlownSpeakerDetected { get; set; } = false;
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

        private const int FFT_SAMPLE_WINDOW = 1024;
        private readonly float[] _fftBuffer = new float[FFT_SAMPLE_WINDOW];
        private int _fftBufferIndex = 0;
        private float _peakThd = 0f;
        private FftAnalysisResult _lastFftResult = new FftAnalysisResult();

        public event Action<float[]> SpectrumUpdated;
        public float[] LatestSpectrum { get; private set; } = new float[16];

        private AcousticAnalyzerService() { }

        public void StartListening()
        {
            try
            {
                if (_isRecording) StopListening();

                if (WaveInEvent.DeviceCount > 0)
                {
                    _currentPeak = 0f;
                    _peakThd = 0f;
                    _fftBufferIndex = 0;
                    Array.Clear(_fftBuffer, 0, _fftBuffer.Length);
                    LatestSpectrum = new float[16];

                    _waveIn = new WaveInEvent
                    {
                        DeviceNumber = 0,
                        WaveFormat = new WaveFormat(44100, 1),
                        BufferMilliseconds = 40
                    };

                    _waveIn.DataAvailable += (s, e) =>
                    {
                        float max = 0;
                        int sampleCount = e.BytesRecorded / 2;

                        for (int index = 0; index < e.BytesRecorded; index += 2)
                        {
                            short rawSample = (short)((e.Buffer[index + 1] << 8) | e.Buffer[index]);
                            float sample32 = rawSample / 32768f;

                            float abs = Math.Abs(sample32);
                            if (abs > max) max = abs;

                            _fftBuffer[_fftBufferIndex++] = sample32;
                            if (_fftBufferIndex >= FFT_SAMPLE_WINDOW)
                            {
                                _fftBufferIndex = 0;
                                var fftRes = AcousticFftAnalyzer.Analyze(_fftBuffer, 44100);
                                _lastFftResult = fftRes;

                                if (fftRes.TotalHarmonicDistortionPercent > _peakThd)
                                {
                                    _peakThd = fftRes.TotalHarmonicDistortionPercent;
                                }

                                LatestSpectrum = fftRes.BandEnergies;
                                SpectrumUpdated?.Invoke(LatestSpectrum);
                            }
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
            float thd = _peakThd > 0 ? _peakThd : _lastFftResult.TotalHarmonicDistortionPercent;
            bool isBlown = thd >= 8.5f;

            StopListening();

            var res = new AcousticTestResult
            {
                MaxInputLevel = peak,
                TotalHarmonicDistortionPercent = thd,
                IsBlownSpeakerDetected = isBlown
            };

            if (isBlown)
            {
                res.IsCouplingVerified = true;
                res.StatusSummary = $"⚠ SPEAKER DISTORTION DETECTED (THD: {thd:F1}% · Chassis / Voice Coil Rattle)";
                res.AccentHex = "#F85149";
            }
            else if (peak >= 0.04f)
            {
                res.IsCouplingVerified = true;
                res.StatusSummary = $"✓ ACOUSTIC COUPLING & HARMONICS NOMINAL (THD: {thd:F1}% · Transduction: {(int)(peak * 100)}%)";
                res.AccentHex = "#3FB950";
            }
            else
            {
                res.IsCouplingVerified = false;
                res.StatusSummary = "Audio sweep complete · Transduction below threshold (Check Mic / Volume)";
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
