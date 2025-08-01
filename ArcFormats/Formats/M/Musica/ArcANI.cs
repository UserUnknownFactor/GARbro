using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Musica
{
    [Export(typeof(ArchiveFormat))]
    public class AniOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ANI/PAZ"; } }
        public override string Description { get { return "Musica engine animation resource"; } }
        public override uint     Signature { get { return 0x040100; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public AniOpener ()
        {
            Signatures = new uint[] { 0x040100, 0x020100, 0 };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.View.ReadUInt16 (0) != 0x100)
                return null;
            int count = file.View.ReadInt16 (2);
            if (!IsSaneCount (count))
                return null;
            if (file.View.ReadUInt32 (4) != 0)
                return null;
            using (var input = file.CreateStream())
            {
                var base_name = Path.GetFileNameWithoutExtension (file.Name);
                input.Position = 8;
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    var name = input.ReadCString();
                    if (string.IsNullOrWhiteSpace (name))
                        return null;
                    var entry = new Entry {
                        Name = string.Format ("{0}#{1}", base_name, name),
                        Type = "image",
                        Offset = input.Position,
                    };
                    uint width  = input.ReadUInt16();
                    uint height = input.ReadUInt16();
                    ushort bpp  = input.ReadUInt16();
                    entry.Size = width * height * bpp / 8 + 10;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    input.Position = entry.Offset + entry.Size;
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var info = new ImageMetaData();
            info.Width   = input.ReadUInt16();
            info.Height  = input.ReadUInt16();
            info.BPP     = input.ReadUInt16();
            info.OffsetX = input.ReadInt16();
            info.OffsetY = input.ReadInt16();
            return new RawImageDecoder (input, info);
        }
    }

    internal class RawImageDecoder : BinaryImageDecoder
    {
        public RawImageDecoder (IBinaryStream input, ImageMetaData info) : base (input, info)
        {
        }

        protected override ImageData GetImageData ()
        {
            var format = GetFormatFromBpp (Info.BPP);
            m_input.Position = 10;
            int stride = Info.iWidth * Info.BPP / 8;
            var pixels = m_input.ReadBytes (stride * Info.iHeight);
            return ImageData.Create (Info, format, null, pixels, stride);
        }

        static PixelFormat GetFormatFromBpp (int bpp)
        {
            switch (bpp)
            {
            case 32:    return PixelFormats.Bgra32;
            case 24:    return PixelFormats.Bgr24;
            case 16:    return PixelFormats.Bgr565;
            case 8:     return PixelFormats.Gray8;
            default:    throw new InvalidFormatException();
            }
        }
    }
}
