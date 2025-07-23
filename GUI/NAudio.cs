using NAudio.Wave;

namespace GARbro
{
    public class WaveStreamImpl : WaveStream
    {
        GameRes.SoundInput  m_input;
        WaveFormat          m_format;

        public override WaveFormat WaveFormat { get { return m_format; } }

        public override long Position
        {
            get { return m_input.Position; }
            set { m_input.Position = value; }
        }

        public override long Length { get { return m_input.Length; } }

        public WaveStreamImpl (GameRes.SoundInput input)
        {
            m_input = input;
            var format = m_input.Format;
            m_format = WaveFormat.CreateCustomFormat ((WaveFormatEncoding)format.FormatTag,
                                                      (int)format.SamplesPerSecond,
                                                      format.Channels,
                                                      (int)format.AverageBytesPerSecond,
                                                      format.BlockAlign,
                                                      format.BitsPerSample);
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            return m_input.Read (buffer, offset, count);
        }

        public override int ReadByte ()
        {
            return m_input.ReadByte();
        }

        #region IDisposable Members
        bool disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    m_input.Dispose();
                }
                disposed = true;
                base.Dispose (disposing);
            }
        }
        #endregion
    }
}
