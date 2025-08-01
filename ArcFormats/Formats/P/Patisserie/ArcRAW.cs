using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Patisserie
{
    [Export(typeof(ArchiveFormat))]
    public class RawOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ABB/RAW"; } }
        public override string Description { get { return "Patisserie animation resource"; } }
        public override uint     Signature { get { return 0x04574152; } } // 'RAW\x04'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public RawOpener ()
        {
            Extensions = new string[] { "raw" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint end = file.View.ReadUInt32 (4);
            if (end > file.MaxOffset - 8)
                return null;
            end += 8;
            long current_offset = 8;
            var dir = new List<Entry>();
            int i = 0;
            while (current_offset < end)
            {
                var entry = new Entry {
                    Name = string.Format ("{0:D4}", i++),
                    Type = "image",
                    Offset = current_offset,
                };
                uint width  = file.View.ReadUInt32 (current_offset+0xC);
                uint height = file.View.ReadUInt32 (current_offset+0x10);
                entry.Size = 0x14 + 4 * width * height;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                current_offset += entry.Size;
            }
            return new ArcFile (file, this, dir);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var info = new ImageMetaData
            {
                OffsetX = arc.File.View.ReadInt32 (entry.Offset+4),
                OffsetY = arc.File.View.ReadInt32(entry.Offset+8),
                Width  = arc.File.View.ReadUInt32(entry.Offset+0xC),
                Height = arc.File.View.ReadUInt32(entry.Offset+0x10),
                BPP = 32,
            };
            var pixels = arc.File.View.ReadBytes (entry.Offset+0x14, entry.Size-0x14);
            return new RawDecoder (info, pixels);
        }
    }

    internal sealed class RawDecoder : IImageDecoder
    {
        public Stream            Source { get { return null; } }
        public ImageFormat SourceFormat { get { return null; } }
        public ImageMetaData       Info { get; private set; }
        public ImageData          Image { get; private set; }

        public RawDecoder (ImageMetaData info, byte[] pixels)
        {
            Info = info;
            Image = ImageData.Create (info, PixelFormats.Bgra32, null, pixels);
        }

        public void Dispose ()
        {
        }
    }
}
