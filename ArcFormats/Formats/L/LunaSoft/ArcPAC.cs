using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.LunaSoft
{
    [Export(typeof(ArchiveFormat))]
    public class PacOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAC/LUNA"; } }
        public override string Description { get { return "LunaSoft resource archive"; } }
        public override uint     Signature { get { return 0xAD82CF82; } } // ぱく
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            long base_offset = file.View.ReadUInt32 (8);
            var dir = new List<Entry> (count);
            if (!ReadIndex (file, dir, count, base_offset) &&
                !ReadIndex (file, dir, count, base_offset, true))
                return null;
            return new ArcFile (file, this, dir);
        }

        bool ReadIndex (ArcView file, List<Entry> dir, int count, long base_offset, bool long_offsets = false)
        {
            Func<uint, long> read_offset;
            uint size_pos;
            if (long_offsets)
            {
                read_offset = idx_off => file.View.ReadInt64 (idx_off);
                size_pos = 0x108;
            }
            else
            {
                read_offset = idx_off => file.View.ReadUInt32 (idx_off);
                size_pos = 0x104;
            }
            dir.Clear();
            uint current_offset = 0x10;
            for (int i = 0; i < count; ++i)
            {
                uint name_length = file.View.ReadUInt32 (current_offset+size_pos+4);
                if (name_length > 0x100 || 0 == name_length)
                    return false;
                var name = file.View.ReadString (current_offset, name_length);
                var entry = Create<Entry> (name);
                entry.Offset = base_offset + read_offset (current_offset+0x100);
                entry.Size = file.View.ReadUInt32 (current_offset+size_pos);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return false;
                dir.Add (entry);
                current_offset += size_pos+8;
            }
            return true;
        }
    }
}
