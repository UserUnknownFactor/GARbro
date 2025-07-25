using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media.Imaging;
using GameRes.Utility;

// [030214][Mink] Danger Angel ~Ijou Shinka~

namespace GameRes.Formats.Mink
{
    internal class MinkMetaData : ImageMetaData
    {
        public int  UnpackedSize;
    }

    [Export(typeof(ImageFormat))]
    public class DatFormat : ImageFormat
    {
        public override string         Tag { get { return "GDF/BMP"; } }
        public override string Description { get { return "Mink compressed bitmap"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (9);
            int packed_size = header.ToInt32 (1);
            int unpacked_size = header.ToInt32 (5);
            if (header[0] != 3 || packed_size != file.Length - 9)
                return null;
            var bmp_header = new byte[56];
            Unpack (file, bmp_header);
            using (var bmp = new BinMemoryStream (bmp_header))
            {
                var info = Bmp.ReadMetaData (bmp);
                if (null == info)
                    return null;
                return new MinkMetaData {
                    Width = info.Width,
                    Height = info.Height,
                    BPP = info.BPP,
                    UnpackedSize = unpacked_size,
                };
            }
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (MinkMetaData)info;
            file.Position = 9;
            var pixels = new byte[meta.UnpackedSize];
            Unpack (file, pixels);
            using (var bmp = new BinMemoryStream (pixels, file.Name))
                return Bmp.Read (bmp, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("DatFormat.Write not implemented");
        }

        void Unpack (IBinaryStream input, byte[] output)
        {
            int offset_bits = input.ReadUInt8();
            int count_bits  = input.ReadUInt8();
            int dst = 0;
            using (var bits = new MsbBitStream (input.AsStream, true))
            {
                while (dst < output.Length)
                {
                    if (bits.GetNextBit() != 0)
                    {
                        int v = bits.GetBits (8);
                        if (v < 0)
                            break;
                        output[dst++] = (byte)v;
                    }
                    else
                    {
                        int src = bits.GetBits (offset_bits);
                        if (src < 0)
                            break;
                        int count = bits.GetBits (count_bits);
                        if (count < 0)
                            break;
                        count = Math.Min (output.Length-dst, count+1);
                        Binary.CopyOverlapped (output, src, dst, count);
                        dst += count;
                    }
                }
            }
        }
    }
}
