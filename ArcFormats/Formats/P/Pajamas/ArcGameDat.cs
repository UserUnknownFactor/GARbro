using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Pajamas
{
    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "GAMEDAT"; } }
        public override string Description { get { return "Pajamas Adventure System resource archive"; } }
        public override uint     Signature { get { return 0x454d4147; } } // 'GAME'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public DatOpener ()
        {
            Extensions = new string[] { "dat", "pak" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "GAMEDAT PAC"))
                return null;
            int version = file.View.ReadByte (0x0b);
            if ('K' == version)
                version = 1;
            else if ('2' == version)
                version = 2;
            else
                return null;
            int count = file.View.ReadInt32 (0x0c);
            if (count <= 0 || count > 0xfffff)
                return null;
            int name_length = 1 == version ? 16 : 32;

            int name_offset = 0x10;
            int index_offset = name_offset + name_length*count;
            int base_offset = index_offset + 8*count;
            if ((uint)base_offset > file.View.Reserve (0, (uint)base_offset))
                return null;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (name_offset, (uint)name_length);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = base_offset + file.View.ReadUInt32 (index_offset);
                entry.Size = file.View.ReadUInt32 (index_offset+4);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                name_offset += name_length;
                index_offset += 8;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (!entry.Name.EndsWith ("textdata.bin", StringComparison.InvariantCultureIgnoreCase))
                return arc.File.CreateStream (entry.Offset, entry.Size);
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            // encrypted PJADV
            if (0x95 == data[0] && 0x6B == data[1] && 0x3C == data[2]
                && 0x9D == data[3] && 0x63 == data[4])
            {
                byte key = 0xC5;
                for (int i = 0; i < data.Length; ++i)
                {
                    data[i] ^= key;
                    key += 0x5C;
                }
            }
            return new BinMemoryStream (data, entry.Name);
        }
    }
}
