//! \file       AudioFLAC.cs
//! \date       2024 Jan 15
//! \brief      Free Lossless Audio Codec format.
//
// Copyright (C) 2025 by morkt and others
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
using NAudio.MediaFoundation;

namespace GameRes.Formats
{
    [Export(typeof(AudioFormat))]
    public class FlacAudio : AudioFormat
    {
        public override string         Tag { get { return "FLAC"; } }
        public override string Description { get { return "Free Lossless Audio Codec"; } }
        public override uint     Signature { get { return 0x43614C66; } } // 'fLaC'
        public override bool      CanWrite { get { return false; } }

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
        private StreamMediaFoundationReader m_reader;
        private int m_bitrate;

        public override long Position
        {
            get { return m_reader.Position; }
            set { m_reader.Position = value; }
        }

        public override bool CanSeek { get { return m_reader.CanSeek; } }

        public override int SourceBitrate
        {
            get { return m_bitrate; }
        }

        public override string SourceFormat { get { return "flac"; } }

        public FlacInput(Stream file) : base(file)
        {
            m_reader = new StreamMediaFoundationReader(file);
            var format = m_reader.WaveFormat;

            if (m_reader.TotalTime.TotalSeconds > 0)
                m_bitrate = (int)(file.Length * 8 / m_reader.TotalTime.TotalSeconds);
            else
                m_bitrate = format.AverageBytesPerSecond * 8;

            this.Format = new GameRes.WaveFormat
            {
                FormatTag = (ushort)format.Encoding,
                Channels = (ushort)format.Channels,
                SamplesPerSecond = (uint)format.SampleRate,
                BitsPerSample = (ushort)format.BitsPerSample,
                BlockAlign = (ushort)format.BlockAlign,
                AverageBytesPerSecond = (uint)format.AverageBytesPerSecond,
            };

            this.PcmSize = m_reader.Length;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return m_reader.Read(buffer, offset, count);
        }

        #region IDisposable Members
        private bool _flac_disposed;
        protected override void Dispose(bool disposing)
        {
            if (!_flac_disposed)
            {
                if (disposing)
                    m_reader?.Dispose();
                _flac_disposed = true;
                base.Dispose(disposing);
            }
        }
        #endregion
    }
}