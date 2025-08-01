using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Kogado
{
    [Export(typeof(ImageFormat))]
    public class LpgFormat : ImageFormat
    {
        public override string         Tag { get { return "LPG"; } }
        public override string Description { get { return "Kogado Studio image format"; } }
        public override uint     Signature { get { return 1; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension ("LPG"))
                return null;
            var header = file.ReadHeader (0x1C);
            int bpp = header.ToInt32 (4);
            uint width  = header.ToUInt32 (0x10);
            uint height = header.ToUInt32 (0x14);
            if (bpp != 32 && bpp != 24
                || 0 == width || width > 0x8000 || 0 == height || height > 0x8000)
                return null;
            return new ImageMetaData { Width = width, Height = height, BPP = bpp };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Position = 0x1C;
            var palette = ReadPalette (file.AsStream);
            if (info.BPP != 32)
            {
                var pixels = new byte[info.iWidth * info.iHeight];
                file.Read (pixels, 0, pixels.Length);
                return ImageData.CreateFlipped (info, PixelFormats.Indexed8, palette, pixels, info.iWidth);
            }
            else
            {
                int stride = info.iWidth * 4;
                var pixels = new byte[stride * info.iHeight];
                var colormap = palette.Colors;
                for (int dst = 0; dst < pixels.Length; dst += 4)
                {
                    byte c = file.ReadUInt8();
                    pixels[dst  ] = colormap[c].B;
                    pixels[dst+1] = colormap[c].G;
                    pixels[dst+2] = colormap[c].R;
                    pixels[dst+3] = file.ReadUInt8();
                }
                return ImageData.CreateFlipped (info, PixelFormats.Bgra32, null, pixels, stride);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("LpgFormat.Write not implemented");
        }
    }
}
