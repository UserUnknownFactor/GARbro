using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

namespace GameRes.Formats.BellDa
{
    [Export(typeof(ImageFormat))]
    public class CpFormat : ImageFormat
    {
        public override string         Tag { get { return "BMP/CP"; } }
        public override string Description { get { return "BELL-DA compressed bitmap"; } }
        public override uint     Signature { get { return 0; } }

        public CpFormat ()
        {
            Signatures = new uint[] { 0x42FD5043, 0 };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (5);
            if (!header.AsciiEqual (0, "CP") || (header[2] & 0xC0) != 0xC0 ||
                !header.AsciiEqual (3, "BM"))
                return null;
            using (var bmp = OpenCompressed (file))
                return Bmp.ReadMetaData (bmp);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            using (var bmp = OpenCompressed (file))
                return Bmp.Read (bmp, info);
        }

        internal IBinaryStream OpenCompressed (IBinaryStream file)
        {
            Stream input = new StreamRegion (file.AsStream, 2, true);
            input = new PackedStream<CpLzssDecompressor> (input);
            return new BinaryStream (input, file.Name);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("CpFormat.Write not implemented");
        }
    }

    internal class CpLzssDecompressor : Decompressor
    {
        Stream          m_input;

        public override void Initialize (Stream input)
        {
            m_input = input;
        }

        protected override IEnumerator<int> Unpack ()
        {
            byte[] frame = new byte[0x1000];
            int frame_pos = 1;
            const int frame_mask = 0xFFF;
            for (;;)
            {
                int ctl = m_input.ReadByte();
                if (-1 == ctl)
                    yield break;
                for (int bit = 0x80; bit != 0; bit >>= 1)
                {
                    if (0 != (ctl & bit))
                    {
                        int b = m_input.ReadByte();
                        if (-1 == b)
                            yield break;
                        frame[frame_pos++ & frame_mask] = (byte)b;
                        m_buffer[m_pos++] = (byte)b;
                        if (0 == --m_length)
                            yield return m_pos;
                    }
                    else
                    {
                        int hi = m_input.ReadByte();
                        if (-1 ==hi)
                            yield break;
                        int lo = m_input.ReadByte();
                        if (-1 == lo)
                            yield break;
                        int offset = hi << 4 | lo >> 4;
                        for (int count = 2 + (lo & 0xF); count > 0; --count)
                        {
                            byte v = frame[offset++ & frame_mask];
                            frame[frame_pos++ & frame_mask] = v;
                            m_buffer[m_pos++] = v;
                            if (0 == --m_length)
                                yield return m_pos;
                        }
                    }
                }
            }
        }
    }
}
