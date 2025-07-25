using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Compression;
using GameRes.Utility;

namespace GameRes.Formats.Gs
{
    internal class PicMetaData : ImageMetaData
    {
        public uint PackedSize;
        public uint UnpackedSize;
        public uint HeaderSize;
        public int  Extra;
    }

    [Export(typeof(ImageFormat))]
    public class PicFormat : ImageFormat
    {
        public override string         Tag { get { return "GsPIC"; } }
        public override string Description { get { return "GsPack image format"; } }
        public override uint     Signature { get { return 0x00040000; } }

        public PicFormat ()
        {
            Extensions = new string[] { "pic" }; // made-up
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            file.Position = 4;
            var info = new PicMetaData();
            info.PackedSize = file.ReadUInt32();
            info.UnpackedSize = file.ReadUInt32();
            info.HeaderSize = file.ReadUInt32();
            if (info.HeaderSize >= file.Length || info.PackedSize + info.HeaderSize > file.Length)
                return null;
            file.ReadUInt32();
            info.Width = file.ReadUInt32();
            info.Height = file.ReadUInt32();
            info.BPP = file.ReadInt32();
            if (info.HeaderSize >= 0x2C)
            {
                info.Extra = file.ReadInt32();
                info.OffsetX = file.ReadInt32();
                info.OffsetY = file.ReadInt32();
            }
            return info;
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (PicMetaData)info;
            file.Position = meta.HeaderSize;
            using (var input = new LzssStream (file.AsStream, LzssMode.Decompress, true))
            {
                BitmapPalette palette = null;
                PixelFormat format;
                if (8 == meta.BPP) // read palette
                {
                    format = PixelFormats.Indexed8;
                    palette = ReadPalette (input);
                }
                else if (24 == meta.BPP)
                    format = PixelFormats.Bgr24;
                else if (16 == meta.BPP)
                    format = PixelFormats.Bgr565;
                else
                    format = PixelFormats.Bgr32;

                int stride = (int)meta.Width*((info.BPP+7)/8);
                var pixels = new byte[stride*meta.Height];
                input.Read (pixels, 0, pixels.Length);
                if (32 == meta.BPP && meta.Extra != 0)
                {
                    for (int i = 3; i < pixels.Length; i += 4)
                    {
                        if (0 != pixels[i])
                        {
                            format = PixelFormats.Bgra32;
                            break;
                        }
                    }
                }
                return ImageData.Create (meta, format, palette, pixels);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("PicFormat.Write not implemented");
        }
    }
}
