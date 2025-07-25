using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.BeF
{
    [Export(typeof(ImageFormat))]
    public class AloFormat : ImageFormat
    {
        public override string         Tag { get { return "ALO"; } }
        public override string Description { get { return "Obfuscated bitmap"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return true; } }

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            if (!stream.Name.HasExtension (".alo"))
                return null;
            var header = stream.ReadHeader (2);
            if (0 != header[0] || 0 != header[1])
                return null;
            using (var bmp = OpenAsBitmap (stream))
                return Bmp.ReadMetaData (bmp);
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            using (var bmp = OpenAsBitmap (stream))
                return Bmp.Read (bmp, info);
        }

        IBinaryStream OpenAsBitmap (IBinaryStream input)
        {
            var header = new byte[2] { (byte)'B', (byte)'M' };
            Stream stream = new StreamRegion (input.AsStream, 2, true);
            stream = new PrefixStream (header, stream);
            return new BinaryStream (stream, input.Name);
        }

        public override void Write (Stream file, ImageData image)
        {
            using (var bmp = new MemoryStream())
            {
                Bmp.Write (bmp, image);
                file.WriteByte (0);
                file.WriteByte (0);
                bmp.Position = 2;
                bmp.CopyTo (file);
            }
        }
    }
}
