using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

namespace GameRes.Formats.Ikura
{
    [Export(typeof(ImageFormat))]
    public class DoFormat : ImageFormat
    {
        public override string         Tag { get { return "VRS/DO"; } }
        public override string Description { get { return "D.O. image format"; } }
        public override uint     Signature { get { return 0x4F44; } } // 'DO'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (8);
            return new ImageMetaData {
                Width = header.ToUInt16 (4),
                Height = header.ToUInt16 (6),
                BPP = 8,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new DoReader (file, info);
            reader.Unpack();
            return ImageData.Create (info, reader.Format, reader.Palette, reader.Data);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("DoFormat.Write not implemented");
        }
    }

    internal class DoReader
    {
        IBinaryStream       m_input;
        byte[]              m_output;

        public PixelFormat    Format { get; private set; }
        public BitmapPalette Palette { get; private set; }
        public byte[]           Data { get { return m_output; } }

        public DoReader (IBinaryStream input, ImageMetaData info)
        {
            m_input = input;
            m_output = new byte[info.Width * info.Height];
            Format = PixelFormats.Indexed8;
        }

        public void Unpack ()
        {
            m_input.Position = 12;
            Palette = ReadPalette();
            int dst = 0;
            while (dst < m_output.Length)
            {
                int count;
                byte ctl = m_input.ReadUInt8();
                if (0 == (ctl & 0xC0))
                {
                    count = ctl & 0x3F;
                    if (0 == count)
                        count = m_input.ReadUInt8() + 0x40;
                    m_input.Read (m_output, dst, count);
                }
                else if (0 == (ctl & 0x80))
                {
                    count = ctl & 0x3F;
                    if (0 == count)
                        count = m_input.ReadUInt8() + 0x40;
                    ++count;
                    Binary.CopyOverlapped (m_output, dst-1, dst, count);
                }
                else
                {
                    int offset = (m_input.ReadUInt8() | (ctl & 0xF) << 8) + 1;
                    count = (ctl >> 4) & 7;
                    if (0 == count)
                        count = m_input.ReadUInt8() + 8;
                    count += 2;
                    Binary.CopyOverlapped (m_output, dst-offset, dst, count);
                }
                dst += count;
            }
        }

        BitmapPalette ReadPalette ()
        {
            var palette_data = m_input.ReadBytes (0x300);
            if (palette_data.Length != 0x300)
                throw new EndOfStreamException();
            int src = 0;
            var color_map = new Color[0x100];
            for (int i = 0; i < 0x100; ++i)
            {
                color_map[i] = Color.FromRgb (palette_data[src+1], palette_data[src+2], palette_data[src]);
                src += 3;
            }
            return new BitmapPalette (color_map);
        }
    }
}
