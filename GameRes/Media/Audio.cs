using System;
using System.IO;
using System.Linq;

namespace GameRes
{
    public struct WaveFormat
    {
        public ushort FormatTag;
        public ushort Channels;
        public   uint SamplesPerSecond;
        public   uint AverageBytesPerSecond;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort ExtraSize;

        public void SetBPS ()
        {
            AverageBytesPerSecond = (uint)(SamplesPerSecond * Channels * BitsPerSample / 8);
        }
    }

    public abstract class SoundInput : Stream
    {
        public abstract int   SourceBitrate { get; }
        public abstract string SourceFormat { get; }

        public WaveFormat Format { get; protected set; }
        public Stream     Source { get; protected set; }
        public long      PcmSize { get; protected set; }


        protected SoundInput (Stream input)
        {
            Source = input;
        }

        public virtual void Reset ()
        {
            Position = 0;
        }

        #region System.IO.Stream methods
        public override bool  CanRead { get { return Source.CanRead; } }
        public override bool CanWrite { get { return false; } }
        public override long   Length { get { return PcmSize; } }

        public override void Flush()
        {
            Source.Flush();
        }

        public override long Seek(long offset, SeekOrigin origin = SeekOrigin.Current)
        {
            long newPosition;

            switch (origin)
            {
            case SeekOrigin.Begin:
                newPosition = offset;
                break;
            case SeekOrigin.End:
                newPosition = Length + offset;
                break;
            case SeekOrigin.Current:
            default:
                newPosition = Position + offset;
                break;
            }

            if (newPosition < 0)
                newPosition = 0;
                //throw new IOException("Seek operation would position to negatove offset");
            else if (newPosition > Length)
                newPosition = Length;
                //throw new IOException("Seek operation would position beyond stream length");

            Position = newPosition;
            return newPosition;
        }

        public override void SetLength (long length)
        {
            throw new System.NotSupportedException ("SoundInput.SetLength method is not supported");
        }

        public override void Write (byte[] buffer, int offset, int count)
        {
            throw new System.NotSupportedException ("SoundInput.Write method is not supported");
        }

        public override void WriteByte (byte value)
        {
            throw new System.NotSupportedException ("SoundInput.WriteByte method is not supported");
        }
        #endregion

        #region IDisposable Members
        bool disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    Source.Dispose();
                }
                disposed = true;
                base.Dispose (disposing);
            }
        }
        #endregion
    }

    /// <summary>
    /// Class representing raw PCM sound input.
    /// </summary>
    public class RawPcmInput : SoundInput
    {
        public override string SourceFormat { get { return "raw"; } }

        public override int SourceBitrate
        {
            get { return (int)Format.AverageBytesPerSecond * 8; }
        }

        public RawPcmInput (Stream file, WaveFormat format) : base (file)
        {
            this.Format = format;
            this.PcmSize = file.Length;
        }

        #region IO.Stream methods
        public override long Position
        {
            get { return Source.Position; }
            set { Source.Position = value; }
        }

        public override bool CanSeek { get { return Source.CanSeek; } }

        public override long Seek (long offset, SeekOrigin origin)
        {
            return Source.Seek (offset, origin);
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            return Source.Read (buffer, offset, count);
        }

        public override int ReadByte ()
        {
            return Source.ReadByte();
        }
        #endregion
    }

    public abstract class AudioFormat : IResource
    {
        public override string Type { get { return "audio"; } }

        public abstract SoundInput TryOpen (IBinaryStream file);

        public virtual void Write (SoundInput source, Stream output)
        {
            throw new System.NotImplementedException ("AudioFormat.Write not implemenented");
        }

        public static AudioFormat FindFormat(IBinaryStream file, bool writable = false)
        {
            var formatImpls = FormatCatalog.Instance.FindFormats<AudioFormat>(file.Name, file.Signature).Where(p => writable ? p.CanWrite : true);
            foreach (var impl in formatImpls)
            {
                try
                {
                    file.Position = 0;
                    using (var input = impl.TryOpen(file))
                    {
                        if (input != null)
                        {
                            if (file.CanSeek)
                                file.Position = 0;
                            return impl;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch { }
            }
            return null;
        }

        public static SoundInput Read (IBinaryStream file)
        {
            foreach (var impl in FormatCatalog.Instance.FindFormats<AudioFormat> (file.Name, file.Signature))
            {
                try
                {
                    file.Position = 0;
                    SoundInput sound = impl.TryOpen (file);
                    if (null != sound)
                        return sound;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (System.Exception X)
                {
                    FormatCatalog.Instance.LastError = X;
                }
            }
            return null;
        }

        public static AudioFormat Wav => s_WavFormat.Value;

        static readonly ResourceInstance<AudioFormat> s_WavFormat = new ResourceInstance<AudioFormat> ("WAV");
    }
}
