using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

// [960405][C's Ware] GLO-RI-A ~Kindan no Ketsuzoku~

namespace GameRes.Formats.CsWare
{
    internal class Arc2Entry : Entry
    {
        public uint Key1;
        public uint Key2;
    }

    [Export(typeof(ArchiveFormat))]
    public class Arc2Opener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/ARC2"; } }
        public override string Description { get { return "C's ware resource archive"; } }
        public override uint     Signature { get { return 0x32637261; } } // 'arc2'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = file.View.ReadUInt32 (8);
            if (index_offset >= file.MaxOffset)
                return null;
            var names = new HashSet<string>();
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x10);
                if (names.Add (name))
                {
                    var entry = Create<Arc2Entry> (name);
                    entry.Offset = file.View.ReadUInt32 (index_offset+0x10);
                    entry.Size   = file.View.ReadUInt32 (index_offset+0x14);
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    entry.Key1   = file.View.ReadUInt32 (index_offset+0x18);
                    entry.Key2   = file.View.ReadUInt32 (index_offset+0x1C);
                    dir.Add (entry);
                }
                index_offset += 0x20;
            }
            foreach (Arc2Entry entry in dir)
            {
                uint signature = file.View.ReadUInt32 (entry.Offset) - (entry.Key1 + entry.Key2);
                entry.ChangeType (AutoEntry.DetectFileType (signature));
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var a2ent = entry as Arc2Entry;
            if (null == a2ent || 0 == (a2ent.Key1 | a2ent.Key2))
                return base.OpenEntry (arc, entry);
            uint length = ((uint)entry.Size + 3) & ~3u;
            var data = new byte[length];
            arc.File.View.Read (entry.Offset, data, 0, entry.Size);
            uint key1 = a2ent.Key1;
            uint key2 = a2ent.Key2;
            unsafe
            {
                fixed (byte* data8 = data)
                {
                    uint* data32 = (uint*)data8;
                    for (uint i = 0; i < length; i += 4)
                    {
                        uint key_sum = key1 + key2;
                        *data32++ -= key_sum;
                        key1 = key2;
                        key2 = key_sum;
                    }
                }
            }
            return new BinMemoryStream (data, 0, (int)entry.Size, entry.Name);
        }
    }
}
