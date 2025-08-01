using GameRes.Compression;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GameRes.Formats.Uran
{
    public class NclMetaData : ImageMetaData
    {
        public int  DataOffset;
    }

    [Export(typeof(ImageFormat))]
    public class NclFormat : ImageFormat
    {
        public override string         Tag { get { return "NCL/URAN"; } }
        public override string Description { get { return "Uran multi-frame image format"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if (!file.Name.HasExtension (".ncl"))
                return null;
            var header = file.ReadHeader (0x10);
            int size = header.ToInt32 (0);
            int name_length = header.ToUInt16 (8);
            if (size <= 1 || 0 == name_length || name_length > 0x100)
                return null;
            int pos = 10 + name_length + 6;
            header = file.ReadHeader (pos + 9);
            var name = header.GetCString (10, name_length);
            if (!name.HasExtension (".bmp"))
                return null;
            int header_size = header.ToUInt16 (pos-2);
            if (header_size < 9 || header_size > 0x40 || pos + header_size + size > file.Length)
                return null;
            uint width  = header.ToUInt32 (pos+1);
            uint height = header.ToUInt32 (pos+5);
            return new NclMetaData {
                Width = width,
                Height = height,
                BPP = 8,
                DataOffset = pos + header_size,
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (NclMetaData)info;
            file.Position = meta.DataOffset;
            Stream input = new NclSubStream (file.AsStream, 10, true);
            try
            {
                int method = input.ReadByte();
                if (2 == method)
                    input = new ZLibStream (input, CompressionMode.Decompress);

                var decoder = new BmpBitmapDecoder (input,
                    BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];
                frame.Freeze();
                return new ImageData (frame, info);
            }
            finally
            {
                input.Dispose();
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("NclFormat.Write not implemented");
        }
    }
}
