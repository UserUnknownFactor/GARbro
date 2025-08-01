using System;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.DarkNiteSystem
{
    internal class YpMetaData : ImageMetaData
    {
        public int  UnpackedSize;
    }

    /// <summary>
    /// Marble YB image format predecessor.
    /// </summary>
    [Export(typeof(ImageFormat))]
    public class YpFormat : ImageFormat
    {
        public override string         Tag { get { return "PRS/YP"; } }
        public override string Description { get { return "DarkNiteSystem image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (8);
            if (!header.AsciiEqual ("YP"))
                return null;
            int unpacked_size = header.ToInt24 (2);
            int packed_size = header.ToInt24 (5);
            var data = LzUnpack (file, 0x36);
            using (var bmp = new BinMemoryStream (data, file.Name))
            {
                var info = Bmp.ReadMetaData (bmp);
                if (null == info)
                    return info;
                return new YpMetaData {
                    Width = info.Width,
                    Height = info.Height,
                    BPP = info.BPP,
                    UnpackedSize = unpacked_size,
                };
            }
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (YpMetaData)info;
            file.Position = 8;
            var data = LzUnpack (file, meta.UnpackedSize);
            using (var bmp = new BinMemoryStream (data, file.Name))
                return Bmp.Read (bmp, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("YpFormat.Write not implemented");
        }

        byte[] LzUnpack (IBinaryStream input, int unpacked_size)
        {
            var output = new byte[unpacked_size];
            var frame = new byte[0x4000];
            int dst = 0;
            int ctl_bits = 0;
            byte ctl_mask = 0;
            int frame_pos = 0;
            while (dst < unpacked_size)
            {
                if (0 == ctl_mask)
                {
                    ctl_bits = input.ReadByte();
                    if (-1 == ctl_bits)
                        break;
                    ctl_mask = 0x80;
                }
                if (0 != (ctl_bits & ctl_mask))
                {
                    byte lo = input.ReadUInt8();
                    byte hi = input.ReadUInt8();
                    int count = Math.Min (CountTable[lo & 0xF], unpacked_size - dst);
                    int offset = hi << 4 | lo >> 4;
                    int src = frame_pos - offset;
                    for (int i = 0; i < count; ++i)
                    {
                        byte v = frame[src++ & 0x3FFF];
                        output[dst++] = v;
                        frame[frame_pos++ & 0x3FFF] = v;
                    }
                }
                else
                {
                    byte v = input.ReadUInt8();
                    output[dst++] = v;
                    frame[frame_pos++ & 0x3FFF] = v;
                }
                ctl_mask >>= 1;
            }
            return output;
        }

        static readonly byte[] CountTable = { 3, 4, 5, 6, 7, 8, 9, 0xA, 0xB, 0xC, 0xE, 0x10, 0x18, 0x20, 0x40, 0x80 };
    }
}
