using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.CatSystem
{
    [Export(typeof(ArchiveFormat))]
    public class Hg2Opener : ArchiveFormat
    {
        public override string         Tag { get { return "HG2"; } }
        public override string Description { get { return "CatSystem2 engine multi-image"; } }
        public override uint     Signature { get { return 0x322d4748; } } // 'HG-2'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int version = file.View.ReadInt32 (8);
            if (0x20 != version && 0x25 != version)
                return null;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = new List<Entry>();
            long offset = 0xC;
            int i = 0;
            while (offset < file.MaxOffset)
            {
                uint section_size = file.View.ReadUInt32 (offset+0x40);
                int image_id = file.View.ReadInt32 (offset+0x28);
                var entry = new Entry
                {
                    Name = string.Format ("{0}#{1:D4}", base_name, image_id),
                    Type = "image",
                    Offset = offset,
                };
                if (0 == section_size)
                    entry.Size = (uint)(file.MaxOffset - offset);
                else
                    entry.Size = section_size;
                dir.Add (entry);
                if (0 == section_size)
                    break;
                offset += section_size;
                ++i;
            }
            if (dir.Count < 1)
                return null;
            return new ArcFile (file, this, dir);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var offset = entry.Offset;
            var info = new Hg2MetaData
            {
                Version     = arc.File.View.ReadInt32 (8),
                HeaderSize  = arc.File.View.ReadUInt32 (offset+0x24) + 0x24,
                Width       = arc.File.View.ReadUInt32 (offset),
                Height      = arc.File.View.ReadUInt32 (offset+4),
                BPP         = arc.File.View.ReadInt32 (offset+8),
                DataPacked  = arc.File.View.ReadInt32 (offset+0x14),
                DataUnpacked= arc.File.View.ReadInt32 (offset+0x18),
                CtlPacked   = arc.File.View.ReadInt32 (offset+0x1C),
                CtlUnpacked = arc.File.View.ReadInt32 (offset+0x20),
                CanvasWidth = arc.File.View.ReadUInt32 (offset+0x2C),
                CanvasHeight= arc.File.View.ReadUInt32 (offset+0x30),
                OffsetX     = arc.File.View.ReadInt32 (offset+0x34),
                OffsetY     = arc.File.View.ReadInt32 (offset+0x38),
            };
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new Hg2Reader (input, info);
        }
    }
}
