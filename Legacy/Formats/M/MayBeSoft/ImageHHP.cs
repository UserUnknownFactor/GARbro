using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// [960920][May-Be Soft] Blind Games

namespace GameRes.Formats.MayBeSoft
{
    [Export(typeof(ImageFormat))]
    public class HhpFormat : ImageFormat
    {
        public override string         Tag { get { return "HHP"; } }
        public override string Description { get { return "May-Be Soft image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension ("HHP"))
                return null;
            return new ImageMetaData { Width = 640, Height = 400, BPP = 8 };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var reader = new HhpReader (file, info))
            {
                var pixels = reader.Unpack();
                return ImageData.CreateFlipped (info, reader.Format, reader.Palette, pixels, reader.Stride);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("HhpFormat.Write not implemented");
        }
    }

    internal class HhpReader : IDisposable
    {
        MsbBitStream    m_input;
        byte[]          m_output;
        int             m_stride;

        public PixelFormat    Format { get { return PixelFormats.Indexed8; } }
        public int            Stride { get { return m_stride; } }
        public BitmapPalette Palette { get; private set; }

        public HhpReader (IBinaryStream input, ImageMetaData info)
        {
            m_input = new MsbBitStream (input.AsStream, true);
            m_output = new byte[info.Width * info.Height];
            m_stride = (int)info.Width;
        }

        public byte[] Unpack ()
        {
            Palette = ImageFormat.ReadPalette (m_input.Input, 0x100, PaletteFormat.Rgb);
            int dst = -1;
            while (dst < m_output.Length)
            {
                int code = m_input.GetBits (2);
                if (-1 == code)
                    throw new InvalidFormatException();
                int count = m_input.GetBits (LengthTable[code]);
                if (0 == count)
                    break;
                dst += count;
                byte pix = (byte)m_input.GetBits (8);
                m_output[dst] = pix;
                int pos = dst;
                for (;;)
                {
                    code = m_input.GetBits (3);
                    if (-1 == code)
                        throw new InvalidFormatException();
                    if (0 == code)
                        break;
                    if (6 == code)
                    {
                        code = m_input.GetBits (2);
                        count = m_input.GetBits (LengthTable[code]);
                        pos += m_stride * count;
                    }
                    else
                    {
                        pos += m_stride + SkipTable[code];
                        m_output[pos] = pix;
                    }
                }
            }
            byte rep = 0;
            for (int i = 0; i < m_output.Length; ++i)
            {
                if (m_output[i] == 0)
                    m_output[i] = rep;
                else if (m_output[i] == rep)
                    m_output[i] = rep = 0;
                else
                    rep = m_output[i];
            }
            return m_output;
        }

        static readonly byte[] LengthTable = { 4, 6, 8, 0x14 };
        static readonly sbyte[] SkipTable = { 0, 0, 1, -1, 2, -2 };

        bool m_disposed = false;
        public void Dispose ()
        {
            if (!m_disposed)
            {
                m_input.Dispose();
                m_disposed = true;
            }
        }
    }
}
