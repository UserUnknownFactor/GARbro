using System.Collections.Generic;
using System.ComponentModel.Composition;

namespace GameRes.Formats.Will
{
    [Export(typeof(ArchiveFormat))]
    public class Arc0Opener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC0/WILL"; } }
        public override string Description { get { return "Tanaka Tatsuhiro's engine resource archive"; } }
        public override uint     Signature { get { return 0x30435241; } } // 'ARC0'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public Arc0Opener ()
        {
            Extensions = new string[] { "arc" };
            Signatures = new uint[] { 0x30435241, 0x30414654 };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint total_size = file.View.ReadUInt32 (4);
            if (total_size != file.MaxOffset)
                return null;
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;

            var dir = new List<Entry> (count);
            uint index_offset = 0x10;
            for (int i = 0; i < count; ++i)
            {
                uint offset = file.View.ReadUInt32 (index_offset);
                uint size   = file.View.ReadUInt32 (index_offset+4);
                var name = file.View.ReadString (index_offset+0xC, 0x14);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = offset;
                entry.Size = size;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x20;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
