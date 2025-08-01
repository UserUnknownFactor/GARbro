using System.Collections.Generic;
using System.ComponentModel.Composition;

// [030620][Piyopiyo-Gumi] Senryaku Musume 2

namespace GameRes.Formats.Aaru
{
    [Export(typeof(ArchiveFormat))]
    public class Fl3Opener : Fl4Opener
    {
        public override string         Tag { get { return "FL3/AARU"; } }
        public override string Description { get { return "Aaru resource archive"; } }
        public override uint     Signature { get { return 0x2E334C46; } } // 'FL3.0'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.View.ReadByte (4) != '0')
                return null;
            uint data_offset  = file.View.ReadUInt16 (8);
            uint index_size   = file.View.ReadUInt32 (0xA);
            long index_offset = file.View.ReadUInt32 (0xE);
            int count         = file.View.ReadInt32 (0x12);
            if (index_offset + index_size > file.MaxOffset || !IsSaneCount (count))
                return null;
            ushort key   = file.View.ReadUInt16 (0x16);
            ushort flags = file.View.ReadUInt16 (0x18);
            using (var index = file.CreateStream (index_offset, index_size))
            {
                var dir = new List<Entry>();
                for (int i = 0; i < count; ++i)
                {
                    uint size = index.ReadUInt32();
                    if (uint.MaxValue == size)
                        break;
                    int name_length = index.ReadUInt8();
                    var name = index.ReadCString (name_length);
                    var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                    entry.Offset = data_offset;
                    entry.Size   = size;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    data_offset += size;
                    dir.Add (entry);
                }
                if (0 == dir.Count)
                    return null;
                return new ArcFile (file, this, dir);
            }
        }
    }
}
