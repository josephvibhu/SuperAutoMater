using NAudio.Wave;

namespace SuperAutoMater
{
    public class ChannelIsolatorSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider source;
        private readonly bool keepLeft;
        private readonly bool keepRight;

        public WaveFormat WaveFormat => source.WaveFormat;

        public ChannelIsolatorSampleProvider(ISampleProvider source, bool keepLeft, bool keepRight)
        {
            this.source = source;
            this.keepLeft = keepLeft;
            this.keepRight = keepRight;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int samplesRead = source.Read(buffer, offset, count);
            if (source.WaveFormat.Channels == 2)
            {
                for (int i = 0; i < samplesRead; i += 2)
                {
                    if (!keepLeft && offset + i < buffer.Length) buffer[offset + i] = 0f;
                    if (!keepRight && offset + i + 1 < buffer.Length) buffer[offset + i + 1] = 0f;
                }
            }
            return samplesRead;
        }
    }
}