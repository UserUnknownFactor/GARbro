//! \file       AudioFLAC.cs
//! \date       2024 Jan 15
//! \brief      Free Lossless Audio Codec.
//
// Copyright (C) 2024 by morkt
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to
// deal in the Software without restriction, including without limitation the
// rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
// sell copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
// IN THE SOFTWARE.
//

using System;
using System.ComponentModel.Composition;
using System.IO;
using NAudio.Wave;

namespace GameRes.Formats
{
    [Export(typeof(AudioFormat))]
    public class FlacAudio : AudioFormat
    {
        public override string Tag { get { return "FLAC"; } }
        public override string Description { get { return "Free Lossless Audio Codec"; } }
        public override uint Signature { get { return 0x43614C66; } } // 'fLaC'
        public override bool CanWrite { get { return false; } }

        public FlacAudio()
        {
            Extensions = new string[] { "flac" };
        }

        public override SoundInput TryOpen(IBinaryStream file)
        {
            return new FlacInput(file.AsStream);
        }
    }

    public class FlacInput : SoundInput
    {
        int m_bitrate = 0;
        MediaFoundationReader m_reader;
        string m_temp_file;
        long m_file_length;

        public override long Position
        {
            get { return m_reader.Position; }
            set { m_reader.Position = value; }
        }

        public override bool CanSeek { get { return true; } }

        public override int SourceBitrate
        {
            get { return m_bitrate; }
        }

        public override string SourceFormat { get { return "flac"; } }

        public FlacInput(Stream file) : base(file)
        {
            m_temp_file = Path.GetTempFileName();

            try
            {
                using (var tempStream = File.Create(m_temp_file))
                {
                    file.CopyTo(tempStream);
                    m_file_length = tempStream.Length;
                }

                if (file.CanSeek)
                    file.Position = 0;

                m_reader = new MediaFoundationReader(m_temp_file);

                var format = new GameRes.WaveFormat
                {
                    FormatTag = (ushort)m_reader.WaveFormat.Encoding,
                    Channels = (ushort)m_reader.WaveFormat.Channels,
                    SamplesPerSecond = (uint)m_reader.WaveFormat.SampleRate,
                    BitsPerSample = (ushort)m_reader.WaveFormat.BitsPerSample,
                    BlockAlign = (ushort)m_reader.BlockAlign,
                    AverageBytesPerSecond = (uint)m_reader.WaveFormat.AverageBytesPerSecond,
                };
                this.Format = format;
                this.PcmSize = m_reader.Length;

                if (m_reader.TotalTime.TotalSeconds > 0)
                    m_bitrate = (int)(m_file_length * 8 / m_reader.TotalTime.TotalSeconds);
            }
            catch
            {
                DeleteTempFile();
                throw;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return m_reader.Read(buffer, offset, count);
        }

        private void DeleteTempFile()
        {
            if (!string.IsNullOrEmpty(m_temp_file))
            {
                try
                {
                    File.Delete(m_temp_file);
                }
                catch {}
                m_temp_file = null;
            }
        }

        #region IDisposable Members
        bool m_disposed;
        protected override void Dispose(bool disposing)
        {
            if (!m_disposed)
            {
                if (disposing)
                    m_reader?.Dispose();

                DeleteTempFile();

                m_disposed = true;
                base.Dispose(disposing);
            }
        }
        #endregion
    }
}