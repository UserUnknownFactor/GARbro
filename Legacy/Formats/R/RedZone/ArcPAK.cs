using System.Collections.Generic;
using System.ComponentModel.Composition;

// [010706][RED-ZONE] Kenkyuu Nisshi

namespace GameRes.Formats.RedZone
{
    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag => "PAK/REDZONE";
        public override string Description => "RED-ZONE resource archive";
        public override uint     Signature => 0;
        public override bool  IsHierarchic => false;
        public override bool      CanWrite => false;

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;

            uint index_offset = 4;
            const uint index_entry_size = 0x54;
            long min_offset = index_offset + count * index_entry_size;
            if (min_offset >= file.MaxOffset)
                return null;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x44);
                var entry = Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x44);
                entry.Size   = file.View.ReadUInt32 (index_offset+0x48);
                if (entry.Offset < min_offset || !entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += index_entry_size;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
