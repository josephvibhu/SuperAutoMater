using System;

namespace SuperAutoMater.Wpf.Services
{
    public class FftAnalysisResult
    {
        public float[] BandEnergies { get; set; } = new float[16];
        public float FundamentalFreqHz { get; set; } = 0f;
        public float TotalHarmonicDistortionPercent { get; set; } = 0f;
        public bool IsBlownSpeakerDetected => TotalHarmonicDistortionPercent >= 8.5f;
        public string DistortionSummary => IsBlownSpeakerDetected
            ? $"⚠ SPEAKER DISTORTION DETECTED (THD: {TotalHarmonicDistortionPercent:F1}% · Chassis / Voice Coil Rattle)"
            : $"✓ ACOUSTIC HARMONIC RESPONSE NOMINAL (THD: {TotalHarmonicDistortionPercent:F1}%)";
        public string AccentHex => IsBlownSpeakerDetected ? "#F85149" : "#3FB950";
    }

    public static class AcousticFftAnalyzer
    {
        private const int FFT_SIZE = 1024;
        private static readonly double[] _window = PrecomputeHannWindow(FFT_SIZE);

        // Center frequencies for the 16 display bands
        public static readonly float[] BandCenterFreqs = new float[]
        {
            60f, 110f, 200f, 320f, 500f, 750f, 1100f, 1500f,
            2100f, 3000f, 4200f, 6000f, 8500f, 11500f, 14500f, 18000f
        };

        private static double[] PrecomputeHannWindow(int size)
        {
            var w = new double[size];
            for (int i = 0; i < size; i++)
            {
                w[i] = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (size - 1)));
            }
            return w;
        }

        public static FftAnalysisResult Analyze(float[] samples, int sampleRate = 44100)
        {
            var result = new FftAnalysisResult();
            if (samples == null || samples.Length < FFT_SIZE) return result;

            double[] real = new double[FFT_SIZE];
            double[] imag = new double[FFT_SIZE];

            for (int i = 0; i < FFT_SIZE; i++)
            {
                real[i] = samples[i] * _window[i];
                imag[i] = 0;
            }

            ComputeFft(real, imag);

            // Compute power spectrum (first FFT_SIZE / 2 bins)
            int halfSize = FFT_SIZE / 2;
            double[] magnitudes = new double[halfSize];
            double binResolution = (double)sampleRate / FFT_SIZE;

            double maxMag = 0.0;
            int peakBin = 0;

            for (int i = 1; i < halfSize; i++) // skip DC at index 0
            {
                double mag = Math.Sqrt(real[i] * real[i] + imag[i] * imag[i]) / (FFT_SIZE / 2);
                magnitudes[i] = mag;
                if (mag > maxMag)
                {
                    maxMag = mag;
                    peakBin = i;
                }
            }

            result.FundamentalFreqHz = (float)(peakBin * binResolution);

            // Map magnitudes into 16 frequency bands
            float[] bands = new float[16];
            double[] bandFreqEdges = new double[]
            {
                40, 80, 150, 250, 400, 600, 900, 1300,
                1800, 2500, 3500, 5000, 7000, 10000, 13000, 16000, 20000
            };

            for (int b = 0; b < 16; b++)
            {
                double lowFreq = bandFreqEdges[b];
                double highFreq = bandFreqEdges[b + 1];

                int startBin = Math.Max(1, (int)(lowFreq / binResolution));
                int endBin = Math.Min(halfSize - 1, (int)(highFreq / binResolution));

                double bandSum = 0.0;
                int count = 0;
                for (int bin = startBin; bin <= endBin; bin++)
                {
                    bandSum += magnitudes[bin];
                    count++;
                }

                double avg = count > 0 ? (bandSum / count) : 0;
                // Logarithmic scaling for visual responsiveness
                float normalized = (float)Math.Clamp(Math.Log10(1.0 + avg * 80.0) / 1.8, 0.0, 1.0);
                bands[b] = normalized;
            }

            result.BandEnergies = bands;

            // Calculate Total Harmonic Distortion (THD) if fundamental has adequate energy (> 0.02)
            if (maxMag >= 0.02 && peakBin >= 3 && peakBin < halfSize / 4)
            {
                double harmonicEnergy = 0.0;
                int harmonicsCounted = 0;

                for (int h = 2; h <= 5; h++)
                {
                    int hBin = peakBin * h;
                    if (hBin < halfSize)
                    {
                        // Look in +/- 1 bin neighborhood to account for window leakage
                        double localMax = magnitudes[hBin];
                        if (hBin > 0 && magnitudes[hBin - 1] > localMax) localMax = magnitudes[hBin - 1];
                        if (hBin + 1 < halfSize && magnitudes[hBin + 1] > localMax) localMax = magnitudes[hBin + 1];

                        harmonicEnergy += localMax * localMax;
                        harmonicsCounted++;
                    }
                }

                if (harmonicsCounted > 0)
                {
                    double thd = (Math.Sqrt(harmonicEnergy) / maxMag) * 100.0;
                    result.TotalHarmonicDistortionPercent = (float)Math.Clamp(Math.Round(thd, 1), 0.5, 45.0);
                }
                else
                {
                    result.TotalHarmonicDistortionPercent = 1.1f;
                }
            }
            else
            {
                result.TotalHarmonicDistortionPercent = 1.0f;
            }

            return result;
        }

        private static void ComputeFft(double[] real, double[] imag)
        {
            int n = real.Length;

            // Bit reversal permutation
            int j = 0;
            for (int i = 0; i < n - 1; i++)
            {
                if (i < j)
                {
                    double tr = real[i]; real[i] = real[j]; real[j] = tr;
                    double ti = imag[i]; imag[i] = imag[j]; imag[j] = ti;
                }
                int k = n >> 1;
                while (k <= j)
                {
                    j -= k;
                    k >>= 1;
                }
                j += k;
            }

            // Radix-2 Cooley-Tukey FFT computation
            for (int len = 2; len <= n; len <<= 1)
            {
                double angle = -2.0 * Math.PI / len;
                double wlen_r = Math.Cos(angle);
                double wlen_i = Math.Sin(angle);

                for (int i = 0; i < n; i += len)
                {
                    double wr = 1.0;
                    double wi = 0.0;

                    for (int k = 0; k < len / 2; k++)
                    {
                        int u_idx = i + k;
                        int v_idx = i + k + len / 2;

                        double ur = real[u_idx];
                        double ui = imag[u_idx];
                        double vr = real[v_idx] * wr - imag[v_idx] * wi;
                        double vi = real[v_idx] * wi + imag[v_idx] * wr;

                        real[u_idx] = ur + vr;
                        imag[u_idx] = ui + vi;
                        real[v_idx] = ur - vr;
                        imag[v_idx] = ui - vi;

                        double next_wr = wr * wlen_r - wi * wlen_i;
                        wi = wr * wlen_i + wi * wlen_r;
                        wr = next_wr;
                    }
                }
            }
        }
    }
}
