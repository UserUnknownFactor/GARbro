using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Marble
{
    [Export(typeof(ArchiveFormat))]
    public class AnimOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ANIM/MARBLE"; } }
        public override string Description { get { return "Marble engine video"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public AnimOpener ()
        {
            Extensions = new string[] { "" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            if (0x21 != file.View.ReadInt32 (4)) // milliseconds per frame?
                return null;
            int width  = file.View.ReadInt32 (8);
            int height = file.View.ReadInt32 (12);
            if (width <= 0 || width > 0x1000 || height <= 0 || height > 0x1000)
                return null;
            uint audio_size   = file.View.ReadUInt32 (0x10);
            uint audio_offset = file.View.ReadUInt32 (0x14);
            if (audio_size > 0)
            {
                if (audio_offset >= file.MaxOffset || audio_size > file.MaxOffset - audio_offset)
                    return null;
            }
            int table_offset = 0x18 + count * 6;
            var offsets = new uint[count];
            for (int i = 0; i < count; ++i)
            {
                offsets[i] = file.View.ReadUInt32 (table_offset);
                table_offset += 4;
            }
            var sizes = new uint[count];
            for (int i = 0; i < count; ++i)
            {
                sizes[i] = file.View.ReadUInt32 (table_offset);
                table_offset += 4;
            }

            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = new List<Entry> (count+1);
            for (int i = 0; i < count; ++i)
            {
                var entry = new Entry
                {
                    Name = string.Format ("{0}#{1:D5}.jpg", base_name, i),
                    Type = "image",
                    Offset = offsets[i],
                    Size = sizes[i],
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
            }
            if (audio_size > 0)
            {
                var audio = new Entry
                {
                    Name = base_name + "#audio.way",
                    Type = "audio",
                    Offset = audio_offset,
                    Size = audio_size,
                };
                dir.Add (audio);
            }
            return new ArcFile (file, this, dir);
        }
    }
}
