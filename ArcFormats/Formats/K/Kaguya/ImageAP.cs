using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Kaguya
{
    [Export(typeof(ImageFormat))]
    public class ApFormat : ImageFormat
    {
        public override string         Tag { get { return "AP"; } }
        public override string Description { get { return "KaGuYa script engine image format"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return true; } }

        public ApFormat ()
        {
            Extensions = new string[] { "bg_", "cg_", "cgw", "sp_", "aps", "alp", "prs" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            int A = stream.ReadByte();
            int P = stream.ReadByte();
            if ('A' != A || 'P' != P)
                return null;
            var info = new ImageMetaData();
            info.Width = stream.ReadUInt32();
            info.Height = stream.ReadUInt32();
            info.BPP = stream.ReadInt16();
            if (info.Width > 0x8000 || info.Height > 0x8000 || !(32 == info.BPP || 24 == info.BPP))
                return null;
            return info;
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            stream.Position = 12;
            return ReadBitmapData (stream, info);
        }

        protected ImageData ReadBitmapData (IBinaryStream stream, ImageMetaData info)
        {
            int stride = (int)info.Width*4;
            var pixels = new byte[stride*info.Height];
            for (int row = (int)info.Height-1; row >= 0; --row)
            {
                if (stride != stream.Read (pixels, row*stride, stride))
                    throw new InvalidFormatException();
            }
            PixelFormat format = PixelFormats.Bgra32;
            return ImageData.Create (info, format, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            using (var output = new BinaryWriter (file, Encoding.ASCII, true))
            {
                output.Write ((byte)'A');
                output.Write ((byte)'P');
                output.Write (image.Width);
                output.Write (image.Height);
                output.Write ((short)24);
                WriteBitmapData (file, image);
            }
        }

        protected void WriteBitmapData (Stream file, ImageData image)
        {
            var bitmap = image.Bitmap;
            if (bitmap.Format != PixelFormats.Bgra32)
            {
                bitmap = new FormatConvertedBitmap (bitmap, PixelFormats.Bgra32, null, 0);
            }
            int stride = (int)image.Width * 4;
            byte[] row_data = new byte[stride];
            Int32Rect rect = new Int32Rect (0, (int)image.Height, (int)image.Width, 1);
            for (uint row = 0; row < image.Height; ++row)
            {
                --rect.Y;
                bitmap.CopyPixels (rect, row_data, stride, 0);
                file.Write (row_data, 0, row_data.Length);
            }
        }
    }

    [Export(typeof(ImageFormat))]
    public class Ap0Format : ImageFormat
    {
        public override string         Tag { get { return "AP-0"; } }
        public override string Description { get { return "KaGuYa script engine grayscale image format"; } }
        public override uint     Signature { get { return 0x302D5041; } } // 'AP-0'

        public Ap0Format ()
        {
            Extensions = new string[] { "alp" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            var header = stream.ReadHeader (12);
            var info = new ImageMetaData
            {
                Width   = header.ToUInt32 (4),
                Height  = header.ToUInt32 (8),
                BPP     = 8,
            };
            if (info.Width > 0x8000 || info.Height > 0x8000)
                return null;
            return info;
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            stream.Position = 0xC;
            var pixels = new byte[info.Width*info.Height];
            if (pixels.Length != stream.Read (pixels, 0, pixels.Length))
                throw new EndOfStreamException();
            return ImageData.CreateFlipped (info, PixelFormats.Gray8, null, pixels, (int)info.Width);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Ap0Format.Write not implemented");
        }
    }

    [Export(typeof(ImageFormat))]
    public class Ap2Format : ImageFormat
    {
        public override string         Tag { get { return "AP-2"; } }
        public override string Description { get { return "KaGuYa script engine image format"; } }
        public override uint     Signature { get { return 0x322D5041; } } // 'AP-2'

        public Ap2Format ()
        {
            Extensions = new string[] { "alp" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            stream.Position = 4;
            var info = new ImageMetaData();
            info.OffsetX = stream.ReadInt32();
            info.OffsetY = stream.ReadInt32();
            info.Width   = stream.ReadUInt32();
            info.Height  = stream.ReadUInt32();
            info.BPP = 32;
            if (info.Width > 0x8000 || info.Height > 0x8000)
                return null;
            return info;
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            stream.Position = 0x18;
            var pixels = new byte[4*info.Width*info.Height];
            if (pixels.Length != stream.Read (pixels, 0, pixels.Length))
                throw new EndOfStreamException();
            return ImageData.CreateFlipped (info, PixelFormats.Bgra32, null, pixels, 4*(int)info.Width);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Ap2Format.Write not implemented");
        }
    }

    [Export(typeof(ImageFormat))]
    public class Ap3Format : ImageFormat
    {
        public override string         Tag { get { return "AP-3"; } }
        public override string Description { get { return "KaGuYa script engine image format"; } }
        public override uint     Signature { get { return 0x332D5041; } } // 'AP-3'

        public Ap3Format ()
        {
            Extensions = new string[] { "alp" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            stream.Position = 4;
            var info = new ImageMetaData();
            info.OffsetX = stream.ReadInt32();
            info.OffsetY = stream.ReadInt32();
            info.Width   = stream.ReadUInt32();
            info.Height  = stream.ReadUInt32();
            if (info.Width > 0x8000 || info.Height > 0x8000)
                return null;
            info.BPP     = stream.ReadInt32();
            if (info.BPP != 8 && info.BPP != 24 && info.BPP != 32)
                return null;
            return info;
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            stream.Position = 0x18;
            int stride = info.BPP/8 * (int)info.Width;
            var pixels = new byte[stride * (int)info.Height];
            if (pixels.Length != stream.Read (pixels, 0, pixels.Length))
                throw new EndOfStreamException();
            PixelFormat format = 24 == info.BPP ? PixelFormats.Bgr24
                               : 32 == info.BPP ? PixelFormats.Bgra32
                               : PixelFormats.Gray8;
            return ImageData.CreateFlipped (info, format, null, pixels, stride);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("Ap3Format.Write not implemented");
        }
    }
}
