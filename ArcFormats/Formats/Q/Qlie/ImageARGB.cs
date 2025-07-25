using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Qlie
{
    internal class ArgbMetaData : ImageMetaData
    {
        public uint     ImageOffset;
        public uint     ImageLength;
        public uint     MaskLength;
    }

    [Export(typeof(ImageFormat))]
    public class ArgbFormat : ImageFormat
    {
        public override string         Tag { get { return "ARGB"; } }
        public override string Description { get { return "QLIE image format"; } }
        public override uint     Signature { get { return 0x42475241; } } // 'ARGB'

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x19);
            if (!header.AsciiEqual ("ARGBSaveData1\0") || header[0x10] != 3)
                return null;
            const uint image_offset = 0x19;
            uint image_size = header.ToUInt32 (0x11);
            uint mask_size  = header.ToUInt32 (0x15);
            using (var jpeg = OpenStreamRegion (file, image_offset, image_size))
            {
                var info = Jpeg.ReadMetaData (jpeg);
                if (null == info)
                    return null;
                return new ArgbMetaData {
                    Width   = info.Width,
                    Height  = info.Height,
                    BPP     = 32,
                    ImageOffset = image_offset,
                    ImageLength = image_size,
                    MaskLength = mask_size,
                };
            }
        }

        internal IBinaryStream OpenStreamRegion (IBinaryStream file, long offset, uint length)
        {
            var input = new StreamRegion (file.AsStream, offset, length, true);
            return new BinaryStream (input, file.Name);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (ArgbMetaData)info;
            file.Position = meta.ImageOffset;
            var jpeg = new JpegBitmapDecoder (file.AsStream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            BitmapSource bitmap = jpeg.Frames[0];

            file.Position = meta.ImageOffset + meta.ImageLength;
            var png = new PngBitmapDecoder (file.AsStream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            BitmapSource mask = png.Frames[0];
            if (mask.PixelWidth != bitmap.PixelWidth || mask.PixelHeight != bitmap.PixelHeight)
                throw new InvalidFormatException ("ARGB bitmap and mask dimensions mismatch");

            if (bitmap.Format.BitsPerPixel != 32)
                bitmap = new FormatConvertedBitmap (bitmap, PixelFormats.Bgr32, null, 0);
            int stride = bitmap.PixelWidth * 4;
            var pixels = new byte[stride * bitmap.PixelHeight];
            bitmap.CopyPixels (pixels, stride, 0);

            if (mask.Format.BitsPerPixel != 8)
                mask = new FormatConvertedBitmap (mask, PixelFormats.Gray8, null, 0);
            var alpha = new byte[mask.PixelWidth * mask.PixelHeight];
            mask.CopyPixels (alpha, mask.PixelWidth, 0);

            int src = 0;
            for (int dst = 3; dst < pixels.Length; dst += 4)
            {
                pixels[dst] = alpha[src++];
            }
            return ImageData.Create (info, PixelFormats.Bgra32, null, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("ArgbFormat.Write not implemented");
        }
    }
}
