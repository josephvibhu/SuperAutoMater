using System;
using System.Drawing;
using System.IO;
using System.Media;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NAudio.Wave;

namespace SuperAutoMater
{
    public enum AudioTestMode { None, Left, Right, Both }

    public partial class Form1
    {
        private AudioTestMode currentAudioMode = AudioTestMode.None;
        private readonly SemaphoreSlim _audioLock = new SemaphoreSlim(1, 1);

        public async Task PlayAudioTest(bool leftChannel, bool rightChannel, AudioTestMode mode = AudioTestMode.Both, Button triggerBtn = null)
        {
            string originalText = triggerBtn?.Text;
            Color originalColor = triggerBtn is HudButton hbOrig ? hbOrig.HudAccentColor : ForeColor;

            if (triggerBtn != null)
            {
                triggerBtn.Text = mode == AudioTestMode.Left ? "◄ PLAYING..." : mode == AudioTestMode.Right ? "PLAYING... ►" : "PLAYING STEREO...";
                if (triggerBtn is HudButton hb) hb.HudAccentColor = HudTheme.WarnCaution;
                triggerBtn.Invalidate();
            }

            await _audioLock.WaitAsync();
            try
            {
                currentAudioMode = mode;
                MarkTestComplete("Audio");
                await PlayMelodicChimeAsync(leftChannel, rightChannel);
            }
            catch
            {
                try { Console.Beep(leftChannel ? 800 : 1200, 300); } catch { }
            }
            finally
            {
                currentAudioMode = AudioTestMode.None;
                _audioLock.Release();

                if (triggerBtn != null)
                {
                    triggerBtn.Text = mode == AudioTestMode.Left ? "◄ LEFT [✓]" : mode == AudioTestMode.Right ? "RIGHT [✓] ►" : "STEREO BOTH [✓]";
                    if (triggerBtn is HudButton hb) hb.HudAccentColor = HudTheme.PassNominal;
                    triggerBtn.Invalidate();
                }
            }
        }

        public async Task PlayMelodicAudioPingPongAsync()
        {
            await _audioLock.WaitAsync();
            try
            {
                currentAudioMode = AudioTestMode.Left;
                await PlayMelodicChimeAsync(true, false);
                await Task.Delay(150);

                currentAudioMode = AudioTestMode.Right;
                await PlayMelodicChimeAsync(false, true);

                currentAudioMode = AudioTestMode.None;
                MarkTestComplete("Audio");
            }
            finally
            {
                _audioLock.Release();
            }
        }

        private void StopCurrentAudio()
        {
            try
            {
                using (var sp = new SoundPlayer())
                {
                    sp.Stop();
                }
            }
            catch { }
        }

        private async Task PlayMelodicChimeAsync(bool leftChannel, bool rightChannel)
        {
            int sr = 44100;

            // Vibrant 6-note melodic arpeggio (C5, E5, G5, B5, D6, C6)
            double[] noteFreqs = { 523.25, 659.25, 783.99, 987.77, 1174.66, 1046.50 };
            double noteDuration = 0.24; // seconds per note
            double totalDuration = (noteFreqs.Length * noteDuration) + 0.25;
            int totalSamples = (int)(sr * totalDuration);
            int dataBytes = totalSamples * 2 * 2; // 16-bit stereo

            byte[] wavBuffer;
            using (MemoryStream ms = new MemoryStream(44 + dataBytes))
            using (BinaryWriter bw = new BinaryWriter(ms, Encoding.ASCII))
            {
                bw.Write("RIFF".ToCharArray());
                bw.Write(36 + dataBytes);
                bw.Write("WAVEfmt ".ToCharArray());
                bw.Write(16);
                bw.Write((short)1); // PCM
                bw.Write((short)2); // Stereo 2 channels
                bw.Write(sr);
                bw.Write(sr * 4);
                bw.Write((short)4);
                bw.Write((short)16);
                bw.Write("data".ToCharArray());
                bw.Write(dataBytes);

                for (int i = 0; i < totalSamples; i++)
                {
                    double t = (double)i / sr;
                    double sample = 0.0;

                    for (int n = 0; n < noteFreqs.Length; n++)
                    {
                        double noteStart = n * noteDuration;
                        if (t >= noteStart)
                        {
                            double noteT = t - noteStart;
                            double freq = noteFreqs[n];

                            double attack = Math.Min(1.0, noteT / 0.006);
                            double decay = Math.Exp(-noteT * 5.0);
                            double env = attack * decay;

                            if (env > 0.0005)
                            {
                                double tone = Math.Sin(2 * Math.PI * freq * noteT) * 0.70
                                            + Math.Sin(2 * Math.PI * (freq * 2.0) * noteT) * 0.20
                                            + Math.Sin(2 * Math.PI * (freq * 3.0) * noteT) * 0.10;
                                sample += tone * env;
                            }
                        }
                    }

                    sample = Math.Max(-1.0, Math.Min(1.0, sample * 0.90));
                    short val = (short)(sample * 30000);

                    // Strict channel separation: silenced channel receives 0
                    bw.Write(leftChannel ? val : (short)0);
                    bw.Write(rightChannel ? val : (short)0);
                }

                wavBuffer = ms.ToArray();
            }

            await Task.Run(() =>
            {
                // Primary Engine: NAudio low-latency WaveOutEvent
                bool playedWithNaudio = false;
                try
                {
                    using (MemoryStream playStream = new MemoryStream(wavBuffer))
                    using (var reader = new WaveFileReader(playStream))
                    using (var waveOut = new WaveOutEvent { DesiredLatency = 80 })
                    {
                        waveOut.Init(reader);
                        waveOut.Play();
                        while (waveOut.PlaybackState == PlaybackState.Playing)
                        {
                            Thread.Sleep(20);
                        }
                        playedWithNaudio = true;
                    }
                }
                catch { }

                // Fallback Engine: Windows SoundPlayer if NAudio cannot acquire audio endpoint
                if (!playedWithNaudio)
                {
                    try
                    {
                        using (MemoryStream spStream = new MemoryStream(wavBuffer))
                        using (SoundPlayer sp = new SoundPlayer(spStream))
                        {
                            sp.PlaySync();
                        }
                    }
                    catch
                    {
                        try { SystemSounds.Asterisk.Play(); } catch { }
                    }
                }
            });
        }
    }
}
