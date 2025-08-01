using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Noesis
{
    internal class IgaEntry : Entry
    {
        public uint NameOffset;
    }

    [Export(typeof(ArchiveFormat))]
    public class IgaOpener : ArchiveFormat
    {
        public override string         Tag { get { return "IGA"; } }
        public override string Description { get { return "Noesis resource archive"; } }
        public override uint     Signature { get { return 0x30414749; } } // 'IGA0'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            using (var input = file.CreateStream())
            {
                input.Position = 0x10;
                uint index_length = ReadPackedUInt (input);
                var dir = new List<Entry>();
                long end_pos = input.Position + index_length;
                while (input.Position < end_pos)
                {
                    var entry = new IgaEntry();
                    entry.NameOffset = ReadPackedUInt (input);
                    entry.Offset     = ReadPackedUInt (input);
                    entry.Size       = ReadPackedUInt (input);
                    dir.Add (entry);
                }
                uint names_length = ReadPackedUInt (input);
                long data_offset = input.Position + names_length;
                for (int i = 0; i < dir.Count; ++i)
                {
                    var entry = dir[i] as IgaEntry;
                    uint name_length;
                    if (i + 1 < dir.Count)
                        name_length = (dir[i+1] as IgaEntry).NameOffset - entry.NameOffset;
                    else
                        name_length = names_length - entry.NameOffset;
                    entry.Name = ReadPackedString (input, name_length);
                    entry.Type = FormatCatalog.Instance.GetTypeFromName (entry.Name);
                    entry.Offset += data_offset;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            int key = entry.Name.HasExtension (".s") ? 0xFF : 0;
            for (int i = 0; i < data.Length; ++i)
                data[i] ^= (byte)((i + 2) ^ key);
            return new BinMemoryStream (data, entry.Name);
        }

        static uint ReadPackedUInt (IBinaryStream input)
        {
            uint val = 0;
            while ((val & 1) == 0)
            {
                val = val << 7 | input.ReadUInt8();
            }
            return val >> 1;
        }

        static string ReadPackedString (IBinaryStream input, uint length)
        {
            var bytes = new byte[length];
            for (uint i = 0; i < length; ++i)
            {
                bytes[i] = (byte)ReadPackedUInt (input);
            }
            return Encodings.cp932.GetString (bytes);
        }
    }
}
