using System;
using System.IO;
using System.ComponentModel.Composition;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using GameRes.Compression;
using GameRes.Utility;
using System.Collections.Generic;
using System.Linq;

namespace GameRes.Formats.CatSystem
{
    internal class HgMetaData : ImageMetaData
    {
        public uint CanvasWidth;
        public uint CanvasHeight;
        public uint HeaderSize;
    }

    [Export(typeof(ImageFormat))]
    public class Hg3Format : ImageFormat
    {
        public override string         Tag { get { return "HG3"; } }
        public override string Description { get { return "CatSystem engine image format"; } }
        public override uint     Signature { get { return 0x332d4748; } } // 'HG-3'

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (0x4c);
            if (header.ToUInt32 (4) != 0x0c)
                return null;
            if (!header.AsciiEqual (0x14, "stdinfo\0"))
                return null;
            return new HgMetaData
            {
                HeaderSize = header.ToUInt32 (0x1C),
                Width = header.ToUInt32 (0x24),
                Height = header.ToUInt32 (0x28),
                OffsetX = header.ToInt32 (0x30),
                OffsetY = header.ToInt32 (0x34),
                BPP = header.ToInt32 (0x2C),
                CanvasWidth = header.ToUInt32 (0x44),
                CanvasHeight = header.ToUInt32 (0x48),
            };
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var meta = (HgMetaData)info;
            if (32 != meta.BPP && 24 != meta.BPP)
                throw new NotSupportedException ("Not supported HG-3 color depth");

            using (var reg = new StreamRegion (stream.AsStream, 0x14, true))
            using (var input = new BinaryStream (reg, stream.Name))
            using (var reader = new Hg3Reader (input, meta))
            {
                return reader.Image;
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("Hg3Format.Write not implemented");
        }
    }

    internal class HgReader : IImageDecoder
    {
        protected   IBinaryStream   m_input;
        protected   HgMetaData      m_info;
        protected   int             m_pixel_size;
        protected   ImageData       m_image;

        public Stream            Source { get { return m_input.AsStream; } }
        public ImageFormat SourceFormat { get { return null; } }
        public ImageMetaData       Info { get { return m_info; } }
        public virtual ImageData  Image { get { throw new NotImplementedException(); } }
        public int               Stride { get; protected set; }

        protected HgReader (IBinaryStream input, HgMetaData info)
        {
            m_input = input;
            m_info = info;
            m_pixel_size = m_info.BPP / 8;
            Stride = (int)m_info.Width * m_pixel_size;
        }

        public byte[] UnpackStream (long data_offset, int data_packed, int data_unpacked, int ctl_packed, int ctl_unpacked)
        {
            var ctl_offset = data_offset + data_packed;
            var data = new byte[data_unpacked];
            using (var z = new StreamRegion (Source, data_offset, data_packed, true))
            using (var data_in = new ZLibStream (z, CompressionMode.Decompress))
                if (data.Length != data_in.Read (data, 0, data.Length))
                    throw new EndOfStreamException();

            using (var z = new StreamRegion (Source, ctl_offset, ctl_packed, true))
            using (var ctl_in = new ZLibStream (z, CompressionMode.Decompress))
            using (var bits = new LsbBitStream (ctl_in))
            {
                bool copy = bits.GetNextBit() != 0;
                int output_size = GetBitCount (bits);
                var output = new byte[output_size];
                int src = 0;
                int dst = 0;
                while (dst < output_size)
                {
                    int count = GetBitCount (bits);
                    if (copy)
                    {
                        Buffer.BlockCopy (data, src, output, dst, count);
                        src += count;
                    }
                    dst += count;
                    copy = !copy;
                }
                return ApplyDelta (output);
            }
        }

        static int GetBitCount (LsbBitStream bits)
        {
            int n = 0;
            while (0 == bits.GetNextBit())
            {
                ++n;
                if (n >= 0x20)
                    throw new InvalidFormatException ("Overflow at HgReader.GetBitCount");
            }
            int value = 1;
            while (n --> 0)
            {
                value = (value << 1) | bits.GetNextBit();
            }
            return value;
        }

        byte[] ApplyDelta (byte[] pixels)
        {
            var table = new uint[4, 0x100];
            for (uint i = 0; i < 0x100; ++i)
            {
                uint val = i & 0xC0;

                val <<= 6;
                val |= i & 0x30;

                val <<= 6;
                val |= i & 0x0C;

                val <<= 6;
                val |= i & 0x03;

                table[0,i] = val << 6;
                table[1,i] = val << 4;
                table[2,i] = val << 2;
                table[3,i] = val;
            }

            int plane_size = pixels.Length / 4;
            int plane0 = 0;
            int plane1 = plane0 + plane_size;
            int plane2 = plane1 + plane_size;
            int plane3 = plane2 + plane_size;

            byte[] output = new byte[pixels.Length];
            int dst = 0;
            while (dst < output.Length)
            {
                uint val = table[0,pixels[plane0++]] | table[1,pixels[plane1++]]
                         | table[2,pixels[plane2++]] | table[3,pixels[plane3++]];

                output[dst++] = ConvertValue ((byte)val);
                output[dst++] = ConvertValue ((byte)(val >> 8));
                output[dst++] = ConvertValue ((byte)(val >> 16));
                output[dst++] = ConvertValue ((byte)(val >> 24));
            }

            for (int x = m_pixel_size; x < Stride; x++)
            {
                output[x] += output[x - m_pixel_size];
            }

            int prev = 0;
            for (uint y = 1; y < m_info.Height; y++)
            {
                int line = prev + Stride;
                for (int x = 0; x < Stride; x++)
                {
                    output[line+x] += output[prev+x];
                }
                prev = line;
            }
            return output;
        }

        static byte ConvertValue (byte val)
        {
            bool carry = 0 != (val & 1);
            val >>= 1;
            return (byte)(carry ? val ^ 0xFF : val);
        }

        #region IDisposable Members
        public void Dispose ()
        {
            GC.SuppressFinalize (this);
        }
        #endregion
    }

    internal sealed class Hg3Reader : HgReader
    {
        public bool Flipped { get; private set; }

        public override ImageData Image
        {
            get
            {
                if (null == m_image)
                {
                    var pixels = Unpack();
                    PixelFormat format = 24 == m_info.BPP ? PixelFormats.Bgr24 : PixelFormats.Bgra32;
                    if (Flipped)
                        m_image = ImageData.CreateFlipped (Info, format, null, pixels, Stride);
                    else
                        m_image = ImageData.Create (Info, format, null, pixels, Stride);
                }
                return m_image;
            }
        }

        public Hg3Reader (IBinaryStream input, HgMetaData info) : base (input, info)
        {
        }

        public byte[] Unpack ()
        {
            Source.Position = m_info.HeaderSize;
            var img_type = m_input.ReadBytes (8);
            if (Binary.AsciiEqual (img_type, "img0000\0"))
                return UnpackImg0000();
            else if (Binary.AsciiEqual (img_type, "img_jpg\0"))
                return UnpackJpeg();
            else if (Binary.AsciiEqual (img_type, "img_wbp\0"))
                return UnpackWebp();
            else
                throw new NotSupportedException ("Not supported HG-3 image");
        }

        byte[] UnpackImg0000 ()
        {
            Flipped = true;
            Source.Position = m_info.HeaderSize+0x18;
            int packed_data_size = m_input.ReadInt32();
            int data_size = m_input.ReadInt32();
            int packed_ctl_size = m_input.ReadInt32();
            int ctl_size = m_input.ReadInt32();
            return UnpackStream (m_info.HeaderSize+0x28, packed_data_size, data_size, packed_ctl_size, ctl_size);
        }

        byte[] UnpackJpeg ()
        {
            Flipped = false;
            var toc = ReadSections();

            m_input.Position = toc["img_jpg"] + 12;
            var jpeg_size = m_input.ReadInt32();
            BitmapSource frame;
            using (var jpeg = new StreamRegion (Source, Source.Position, jpeg_size, true))
            {
                var decoder = new JpegBitmapDecoder (jpeg, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                frame = decoder.Frames[0];
            }
            if (frame.Format.BitsPerPixel < 24)
                throw new NotSupportedException ("Not supported HG-3 JPEG color depth");
            int src_pixel_size = frame.Format.BitsPerPixel/8;
            int stride = m_info.iWidth * src_pixel_size;
            var pixels = new byte[stride * m_info.iHeight];
            frame.CopyPixels (pixels, stride, 0);

            int total = m_info.iWidth * m_info.iHeight;
            byte[] alpha = null;
            if (toc.ContainsKey ("img_al"))
                alpha = ReadAlpha (toc["img_al"]);
            else
                alpha = Enumerable.Repeat<byte> (0xFF, total).ToArray();

            bool swap_rgb = toc.ContainsKey ("imgmode"); // XXX ???

            var output = new byte[total * 4];
            int src = 0;
            int dst = 0;
            int src_A = 0;
            int src_R = 2, src_G = 1, src_B = 0;
            if (swap_rgb)
            {
                src_R = 0;
                src_B = 2;
            }
            for (uint i = 0; i < total; ++i)
            {
                output[dst++] = pixels[src+src_B];
                output[dst++] = pixels[src+src_G];
                output[dst++] = pixels[src+src_R];
                output[dst++] = alpha[src_A++];
                src += src_pixel_size;
            }
            return output;
        }

        byte[] ReadAlpha (long start_pos)
        {
            m_input.Position = start_pos + 0x10;
            int packed_size = m_input.ReadInt32();
            int alpha_size = m_input.ReadInt32();
            using (var alpha_in = new StreamRegion (Source, Source.Position, packed_size, true))
            using (var alpha = new ZLibStream (alpha_in, CompressionMode.Decompress))
            {
                var alpha_data = new byte[alpha_size];
                alpha.Read (alpha_data, 0, alpha_size);
                return alpha_data;
            }
        }

        Dictionary<string, long> ReadSections ()
        {
            long next_offset = m_info.HeaderSize;
            var toc = new Dictionary<string, long>();
            uint section_size;
            do
            {
                m_input.Position = next_offset;
                var section_name = m_input.ReadCString (8);
                section_size = m_input.ReadUInt32();
                toc[section_name] = next_offset;
                next_offset += section_size;
            }
            while (section_size != 0);
            return toc;
        }

        byte[] UnpackWebp ()
        {
            throw new NotImplementedException ("HG-3 WebP decoder not implemented.");
        }
    }
}
