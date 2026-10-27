using System;
using System.IO;
using System.Media;
using System.Text;
using System.Threading.Tasks;
using SuperAutoMater.Wpf.Core;

namespace VeloxBench.Services;

/// <summary>
/// High-speed Depot Audio Feedback Engine.
/// Generates crisp, non-blocking acoustic cues so technicians running multiple
/// benches simultaneously receive instant auditory verdicts without looking at the screen.
/// </summary>
public sealed class AudioFeedbackService
{
    private static readonly Lazy<AudioFeedbackService> _lazy = new(() => new AudioFeedbackService());
    public static AudioFeedbackService Instance => _lazy.Value;

    private byte[]? _passChimeWav;
    private byte[]? _failChimeWav;
    private byte[]? _warningChimeWav;
    private readonly object _lock = new();

    public bool IsAudioMuted { get; set; } = false;

    private AudioFeedbackService()
    {
        InitializeSynthesizedTones();
    }

    private void InitializeSynthesizedTones()
    {
        try
        {
            _passChimeWav = GenerateDoubleHighBeep();
            _failChimeWav = GenerateLowBuzzTone();
            _warningChimeWav = GenerateWarningTone();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Failed to initialize synthesized audio cues", ex);
        }
    }

    public void PlayPassChime()
    {
        if (IsAudioMuted || _passChimeWav == null) return;
        PlayAsync(_passChimeWav);
    }

    public void PlayFailChime()
    {
        if (IsAudioMuted || _failChimeWav == null) return;
        PlayAsync(_failChimeWav);
    }

    public void PlayWarningChime()
    {
        if (IsAudioMuted || _warningChimeWav == null) return;
        PlayAsync(_warningChimeWav);
    }

    private void PlayAsync(byte[] wavData)
    {
        Task.Run(() =>
        {
            try
            {
                using var ms = new MemoryStream(wavData);
                using var player = new SoundPlayer(ms);
                player.Play();
            }
            catch { }
        });
    }

    /// <summary>
    /// Double high beep: 880 Hz (0.10s) -> gap (0.04s) -> 1320 Hz (0.16s).
    /// </summary>
    private static byte[] GenerateDoubleHighBeep()
    {
        int sampleRate = 44100;
        double duration1 = 0.10;
        double gap = 0.04;
        double duration2 = 0.16;
        double totalDuration = duration1 + gap + duration2;
        int totalSamples = (int)(sampleRate * totalDuration);

        int dataBytes = totalSamples * 2; // 16-bit mono
        using var ms = new MemoryStream(44 + dataBytes);
        using (var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true))
        {
            WriteWavHeader(bw, sampleRate, 1, 16, dataBytes);

            for (int i = 0; i < totalSamples; i++)
            {
                double t = (double)i / sampleRate;
                double sample = 0.0;

                if (t < duration1)
                {
                    double relT = t / duration1;
                    double env = Math.Sin(relT * Math.PI); // smooth sine window
                    sample = Math.Sin(2.0 * Math.PI * 880.0 * t) * env * 0.70;
                }
                else if (t >= duration1 + gap)
                {
                    double t2 = t - (duration1 + gap);
                    double relT2 = t2 / duration2;
                    double env2 = Math.Sin(relT2 * Math.PI);
                    sample = Math.Sin(2.0 * Math.PI * 1320.0 * t2) * env2 * 0.75;
                }

                short val = (short)(sample * 30000);
                bw.Write(val);
            }
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Low buzz warning tone: 180 Hz with subtle odd harmonics for distinct buzz bite.
    /// </summary>
    private static byte[] GenerateLowBuzzTone()
    {
        int sampleRate = 44100;
        double duration = 0.28;
        int totalSamples = (int)(sampleRate * duration);
        int dataBytes = totalSamples * 2;

        using var ms = new MemoryStream(44 + dataBytes);
        using (var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true))
        {
            WriteWavHeader(bw, sampleRate, 1, 16, dataBytes);

            for (int i = 0; i < totalSamples; i++)
            {
                double t = (double)i / sampleRate;
                double relT = t / duration;
                double env = 1.0 - Math.Pow(relT, 1.5); // decay envelope

                // Fundamental (180 Hz) + 3rd harmonic (540 Hz) + 5th harmonic (900 Hz)
                double wave = Math.Sin(2.0 * Math.PI * 180.0 * t) * 0.65
                            + Math.Sin(2.0 * Math.PI * 540.0 * t) * 0.25
                            + Math.Sin(2.0 * Math.PI * 900.0 * t) * 0.10;

                short val = (short)(wave * env * 28000);
                bw.Write(val);
            }
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Downward alert chime: 520 Hz -> 390 Hz.
    /// </summary>
    private static byte[] GenerateWarningTone()
    {
        int sampleRate = 44100;
        double duration = 0.22;
        int totalSamples = (int)(sampleRate * duration);
        int dataBytes = totalSamples * 2;

        using var ms = new MemoryStream(44 + dataBytes);
        using (var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true))
        {
            WriteWavHeader(bw, sampleRate, 1, 16, dataBytes);

            for (int i = 0; i < totalSamples; i++)
            {
                double t = (double)i / sampleRate;
                double relT = t / duration;
                double freq = 520.0 - (130.0 * relT);
                double env = Math.Sin(relT * Math.PI);
                double sample = Math.Sin(2.0 * Math.PI * freq * t) * env * 0.65;

                short val = (short)(sample * 28000);
                bw.Write(val);
            }
        }
        return ms.ToArray();
    }

    private static void WriteWavHeader(BinaryWriter bw, int sampleRate, short channels, short bitsPerSample, int dataBytes)
    {
        bw.Write("RIFF".ToCharArray());
        bw.Write(36 + dataBytes);
        bw.Write("WAVEfmt ".ToCharArray());
        bw.Write(16);
        bw.Write((short)1); // PCM
        bw.Write(channels);
        bw.Write(sampleRate);
        bw.Write(sampleRate * channels * (bitsPerSample / 8));
        bw.Write((short)(channels * (bitsPerSample / 8)));
        bw.Write(bitsPerSample);
        bw.Write("data".ToCharArray());
        bw.Write(dataBytes);
    }
}
