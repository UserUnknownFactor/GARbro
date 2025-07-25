using System;
using System.Diagnostics;
using System.IO;

namespace GameRes.Formats
{
    public class BitStream : IDisposable
    {
        protected Stream    m_input;
        private   bool      m_should_dispose;

        protected int       m_bits = 0;
        protected int       m_cached_bits = 0;

        public Stream  Input { get { return m_input; } }
        public int CacheSize { get { return m_cached_bits; } }

        protected BitStream (Stream file, bool leave_open)
        {
            m_input = file;
            m_should_dispose = !leave_open;
        }

        public void Reset ()
        {
            m_cached_bits = 0;
        }

        #region IDisposable Members
        bool m_disposed = false;

        public void Dispose ()
        {
            Dispose (true);
            GC.SuppressFinalize (this);
        }

        protected virtual void Dispose (bool disposing)
        {
            if (!m_disposed)
            {
                if (disposing && m_should_dispose && null != m_input)
                    m_input.Dispose();
                m_disposed = true;
            }
        }
        #endregion
    }

    public interface IBitStream
    {
        int GetBits (int count);
        int GetNextBit ();
        void Reset ();
    }

    public class MsbBitStream : BitStream, IBitStream
    {
        public MsbBitStream (Stream file, bool leave_open = false)
            : base (file, leave_open)
        {
        }

        public int GetBits (int count)
        {
            Debug.Assert (count <= 24, "MsbBitStream does not support sequences longer than 24 bits");
            while (m_cached_bits < count)
            {
                int b = m_input.ReadByte();
                if (-1 == b)
                    return -1;
                m_bits = (m_bits << 8) | b;
                m_cached_bits += 8;
            }
            int mask = (1 << count) - 1;
            m_cached_bits -= count;
            return (m_bits >> m_cached_bits) & mask;
        }

        public int GetNextBit ()
        {
            return GetBits (1);
        }
    }

    public class LsbBitStream : BitStream, IBitStream
    {
        public LsbBitStream (Stream file, bool leave_open = false)
            : base (file, leave_open)
        {
        }

        public int GetBits (int count)
        {
            Debug.Assert (count <= 32, "LsbBitStream does not support sequences longer than 32 bits");
            int value;
            if (m_cached_bits >= count)
            {
                int mask = (1 << count) - 1;
                value = m_bits & mask;
                m_bits = (int)((uint)m_bits >> count);
                m_cached_bits -= count;
            }
            else
            {
                value = m_bits & ((1 << m_cached_bits) - 1);
                count -= m_cached_bits;
                int shift = m_cached_bits;
                m_cached_bits = 0;
                while (count >= 8)
                {
                    int b = m_input.ReadByte();
                    if (-1 == b)
                        return -1;
                    value |= b << shift;
                    shift += 8;
                    count -= 8;
                }
                if (count > 0)
                {
                    int b = m_input.ReadByte();
                    if (-1 == b)
                        return -1;
                    value |= (b & ((1 << count) - 1)) << shift;
                    m_bits = b >> count;
                    m_cached_bits = 8 - count;
                }
            }
            return value;
        }

        public int GetNextBit ()
        {
            return GetBits (1);
        }
    }
}
