using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.KeroQ
{
    internal class Kgd1MetaData : ImageMetaData
    {
        public int  AlphaLength;
    }

    [Export(typeof(ImageFormat))]
    public class Kgd1Format : ImageFormat
    {
        public override string         Tag { get { return "KGD1"; } }
        public override string Description { get { return "KeroQ image format"; } }
        public override uint     Signature { get { return 0x3144474B; } } // 'KGD1'

        public Kgd1Format ()
        {
            Extensions = new string[] { "kgd" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x18);
            int bpp = header.ToInt16 (6);
            if (bpp != 8 && bpp != 24)
                return null;
            return new Kgd1MetaData {
                Width  = header.ToUInt32 (8),
                Height = header.ToUInt32 (0xC),
                BPP = bpp,
                AlphaLength = header.ToInt32 (0x10),
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (Kgd1MetaData)info;
            file.Position = 0x18;
            byte[] alpha = null;
            if (meta.AlphaLength != 0)
                alpha = file.ReadBytes (meta.AlphaLength);

            BitmapPalette palette = null;
            PixelFormat format;
            if (8 == info.BPP)
            {
                palette = ReadPalette (file.AsStream);
                format = PixelFormats.Indexed8;
            }
            else
                format = PixelFormats.Bgr24;
            var pixels = new byte[(int)info.Width * (int)info.Height * (info.BPP / 8)];
            file.Read (pixels, 0, pixels.Length);
            if (alpha != null)
            {
                pixels = ApplyAlphaChannel (meta, pixels, palette, alpha);
                format = PixelFormats.Bgra32;
            }
            return ImageData.Create (info, format, palette, pixels);
        }

        byte[] ApplyAlphaChannel (ImageMetaData info, byte[] image, BitmapPalette palette, byte[] alpha)
        {
            var output = new byte[4 * (int)info.Width * (int)info.Height];
            if (24 == info.BPP)
            {
                int dst = 0;
                int asrc = 0;
                for (int src = 0; src < image.Length; src += 3)
                {
                    output[dst++] = image[src];
                    output[dst++] = image[src+1];
                    output[dst++] = image[src+2];
                    output[dst++] = (byte)~alpha[asrc++];
                }
            }
            else
            {
                int dst = 0;
                var colors = palette.Colors;
                for (int src = 0; src < image.Length; ++src)
                {
                    byte c = image[src];
                    output[dst++] = colors[c].B;
                    output[dst++] = colors[c].G;
                    output[dst++] = colors[c].R;
                    output[dst++] = (byte)~alpha[src];
                }
            }
            return output;
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Kgd1Format.Write not implemented");
        }
    }
}
