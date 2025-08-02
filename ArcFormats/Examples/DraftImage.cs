using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Unknown
{
    [Export(typeof(ImageFormat))]
    public class xxxFormat : ImageFormat
    {
        public override string         Tag => "xxx";
        public override string Description => "Unknown image format";
        public override uint     Signature => 0;

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (xxxMetaData)info;

            return ImageData.Create (info, format, palette, pixels);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("xxxFormat.Write not implemented");
        }
    }
}
