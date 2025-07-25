using System.Collections.Generic;
using System.ComponentModel.Composition;

// [020125][Luchs] Sollunea

namespace GameRes.Formats.Muse
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/MUSE"; } }
        public override string Description { get { return "Muse engine resource archive"; } }
        public override uint     Signature { get { return 0x4553554D; } } // 'MUSE'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt16 (0x10);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = 0x16;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                uint size   = file.View.ReadUInt32 (index_offset+2);
                uint offset = file.View.ReadUInt32 (index_offset+7);
                var name = file.View.ReadString (index_offset+0xB, 0x100);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = offset;
                entry.Size = size;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x10B;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
