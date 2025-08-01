using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using GameRes.Compression;

// [051111][Software House Parsley] Sonic Princess Platinum Edition

namespace GameRes.Formats.Parsley
{
    internal class CgEntry : Entry
    {
        public ImageMetaData Info;
    }

    [Export(typeof(ArchiveFormat))]
    public class CgV2Opener : ArchiveFormat
    {
        public override string         Tag { get { return "CG/PARSLEY/2"; } }
        public override string Description { get { return "Software House Parsley CG archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public CgV2Opener ()
        {
            Extensions = new string[] { "" };
        }

        const uint DefaultDataOffset = 0x6000;

        public override ArcFile TryOpen (ArcView file)
        {
            if (!VFS.IsPathEqualsToFileName (file.Name, "CG") || file.MaxOffset <= DefaultDataOffset)
                return null;
            uint index_pos = 0;
            var dir = new List<Entry>();
            while (index_pos < DefaultDataOffset)
            {
                if (file.View.ReadByte (index_pos) == 0)
                    break;
                var name = file.View.ReadString (index_pos, 0x20);
                if (string.IsNullOrWhiteSpace (name))
                    return null;
                var entry = Create<CgEntry> (name);
                entry.Offset = file.View.ReadUInt32 (index_pos+0x20) + DefaultDataOffset;
                entry.Size   = file.View.ReadUInt32 (index_pos+0x2C);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.Info = new ImageMetaData {
                    Width  = file.View.ReadUInt32 (index_pos+0x24),
                    Height = file.View.ReadUInt32 (index_pos+0x28),
                    BPP = 8,
                };
                dir.Add (entry);
                index_pos += 0x30;
            }
            if (0 == dir.Count)
                return null;
            return new ArcFile (file, this, dir);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var cgent = (CgEntry)entry;
            var input = arc.File.CreateStream (cgent.Offset, cgent.Size);
            return new Cg2Decoder (input, cgent.Info);
        }

        class Cg2Decoder : BinaryImageDecoder
        {
            public Cg2Decoder (IBinaryStream input, ImageMetaData info) : base (input, info)
            {
            }

            protected override ImageData GetImageData ()
            {
                using (var input = new LzssStream (m_input.AsStream, LzssMode.Decompress, true))
                {
                    int stride = (Info.iWidth + 3) & ~3;
                    var palette = ImageFormat.ReadPalette (input, 0x100);
                    var pixels = new byte[stride * Info.iHeight];
                    input.Read (pixels, 0, pixels.Length);
                    return ImageData.CreateFlipped (Info, PixelFormats.Indexed8, palette, pixels, stride);
                }
            }
        }
    }
}
