using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Aquarium
{
    [Export(typeof(ArchiveFormat))]
    public class AapOpener : ArchiveFormat
    {
        public override string         Tag { get { return "AAP"; } }
        public override string Description { get { return "Aquarium resource archive"; } }
        public override uint     Signature { get { return 0x52415046; } } // 'FPARC10'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "FPARC10\0"))
                return null;
            uint index_offset = file.View.ReadUInt32 (0x10);
            int count = file.View.ReadInt32 (0x14);
            if (index_offset >= file.MaxOffset || !IsSaneCount (count))
                return null;
            uint data_offset = index_offset + (uint)count * 0x30;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x10);
                var entry = Create<PackedEntry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x10) + data_offset;
                entry.UnpackedSize = file.View.ReadUInt32 (index_offset+0x14);
                entry.Size = file.View.ReadUInt32 (index_offset+0x18);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.IsPacked = entry.UnpackedSize != 0;
                dir.Add (entry);
                index_offset += 0x30;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return base.OpenEntry (arc, entry);
            var data = new byte[pent.UnpackedSize];
            using (var input = arc.File.CreateStream (entry.Offset, entry.Size))
                Cp2Reader.DecompressLz (input, data);
            return new BinMemoryStream (data, entry.Name);
        }
    }
}
