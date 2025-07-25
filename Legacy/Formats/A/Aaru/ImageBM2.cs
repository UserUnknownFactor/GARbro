using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Aaru
{
    [Export(typeof(ImageFormat))]
    public class Bm2Format : ImageFormat
    {
        public override string         Tag { get { return "BM2"; } }
        public override string Description { get { return "Aaru bitmap format"; } }
        public override uint     Signature { get { return 0x41324D42; } } // 'BM2A'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x16);
            int bpp = header.ToUInt16 (8);
            if (bpp != 8 && bpp != 24)
                return null;
            return new ImageMetaData {
                BPP     = bpp,
                OffsetX = header.ToInt16 (0xA),
                OffsetY = header.ToInt16 (0xC),
                Width   = header.ToUInt16 (0xE),
                Height  = header.ToUInt16 (0x10),
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new Bm2Reader (file, info);
            var pixels = reader.Unpack();
            return ImageData.Create (info, reader.Format, reader.Palette, pixels, reader.Stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Bm2Format.Write not implemented");
        }
    }

    internal class Bm2Reader
    {
        IBinaryStream   m_input;
        ImageMetaData   m_info;
        int             m_stride;
        byte[]          m_output;

        public BitmapPalette Palette { get; private set; }
        public PixelFormat    Format { get; private set; }
        public int            Stride { get { return m_stride; } }

        public Bm2Reader (IBinaryStream input, ImageMetaData info)
        {
            m_input = input;
            m_info = info;
            m_stride = 4 * (int)m_info.Width;
            m_output = new byte[m_stride * (int)m_info.Height];
            Format = PixelFormats.Bgra32;
        }

        public byte[] Unpack ()
        {
            m_input.Position = 0x16;
            if (8 == m_info.BPP)
                Unpack8bpp();
            else
                Unpack32bpp();
            return m_output;
        }

        void Unpack8bpp ()
        {
            var palette = ImageFormat.ReadColorMap (m_input.AsStream);
            int dst = 0;
            while (dst < m_output.Length)
            {
                byte alpha = m_input.ReadUInt8();
                byte index = m_input.ReadUInt8();
                var color = palette[index];
                m_output[dst++] = color.B;
                m_output[dst++] = color.G;
                m_output[dst++] = color.R;
                m_output[dst++] = alpha;
            }
        }

        void Unpack32bpp ()
        {
            int dst = 0;
            var buffer = new byte[4];
            while (dst < m_output.Length)
            {
                m_input.Read (buffer, 0, 4);
                m_output[dst++] = buffer[1];
                m_output[dst++] = buffer[2];
                m_output[dst++] = buffer[3];
                m_output[dst++] = buffer[0];
            }
        }
    }
}
