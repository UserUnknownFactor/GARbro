using System;
using System.Collections.Generic;
using System.IO;

namespace GameRes.Compression
{
    public interface IStreamFilter : IDisposable
    {
        /// <summary>
        /// Initialize filter on top of specified stream.
        /// </summary>
        void Initialize (Stream input);

        /// <summary>
        /// Whether filter has reached an end.
        /// </summary>
        bool Eof { get; }

        /// <summary>
        /// Continue data extraction from underlying stream.
        /// </summary>
        /// <returns>Returns number of bytes read.</returns>
        int Continue (byte[] buffer, int pos, int count);
    }

    public abstract class Decompressor : IStreamFilter
    {
        IEnumerator<int>    m_unpack;
        protected byte[]    m_buffer;
        protected int       m_pos;
        protected int       m_length;

        public abstract void Initialize (Stream input);

        public bool Eof { get; private set; }

        public int Continue (byte[] buffer, int pos, int count)
        {
            m_buffer = buffer;
            m_pos = pos;
            m_length = count;
            if (null == m_unpack)
                m_unpack = Unpack();
            Eof = !m_unpack.MoveNext();
            return m_pos - pos;
        }

        /// <summary>
        /// Decompression coroutine that updates m_buffer with each MoveNext call.
        /// </summary>
        protected abstract IEnumerator<int> Unpack ();

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
                if (m_unpack != null)
                    m_unpack.Dispose();
                m_disposed = true;
            }
        }
        #endregion
    }

    public class PackedStream<TDecompressor> : GameRes.Formats.InputProxyStream
        where TDecompressor : IStreamFilter, new()
    {
        TDecompressor   m_reader;

        public PackedStream (Stream input, bool leave_open = false) : base (input, leave_open)
        {
            m_reader = new TDecompressor();
            m_reader.Initialize (input);
        }

        public PackedStream (Stream input, TDecompressor reader, bool leave_open = false) : base (input, leave_open)
        {
            m_reader = reader;
            m_reader.Initialize (input);
        }

        protected TDecompressor Reader { get { return m_reader; } }

        public override bool CanSeek  { get { return false; } }
        public override long Length
        {
            get { throw new NotSupportedException ("Stream.Length property is not supported"); }
        }
        public override long Position
        {
            get { throw new NotSupportedException ("Stream.Position property is not supported"); }
            set { throw new NotSupportedException ("Stream.Position property is not supported"); }
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            if (!m_reader.Eof && count > 0)
                return m_reader.Continue (buffer, offset, count);
            return 0;
        }

        public override void Flush()
        {
        }

        public override long Seek (long offset, SeekOrigin origin)
        {
            throw new NotSupportedException ("Stream.Seek method is not supported");
        }

        #region IDisposable Members
        bool m_disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (!m_disposed)
            {
                m_reader.Dispose();
                m_disposed = true;
                base.Dispose (disposing);
            }
        }
        #endregion
    }
}
