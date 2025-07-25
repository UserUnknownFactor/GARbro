using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

// [000623][Mink] Lovely Angels Peropero Candy 2

namespace GameRes.Formats.Mink
{
    [Export(typeof(ArchiveFormat))]
    public class GrpOpener : ArchiveFormat
    {
        public override string         Tag { get { return "GRP/MINK"; } }
        public override string Description { get { return "Mink resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public GrpOpener ()
        {
            Extensions = new string[] { "" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = file.View.ReadUInt32 (4);
            if (index_offset < 8 || index_offset>= file.MaxOffset)
                return null;

            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x18);
                if (string.IsNullOrWhiteSpace (name))
                    return null;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x18);
                entry.Size   = file.View.ReadUInt32 (index_offset+0x1C);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x20;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (!entry.Name.HasExtension (".MSC") || !arc.File.View.AsciiEqual (entry.Offset, "MADSCR"))
                return base.OpenEntry (arc, entry);
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            ushort id = data.ToUInt16 (8);
            byte key;
            if (KnownScriptKeys.TryGetValue (id, out key))
            {
                for (int i = 0x20; i < data.Length; ++i)
                    data[i] ^= key;
            }
            return new BinMemoryStream (data, entry.Name);
        }

        static readonly Dictionary<ushort, byte> KnownScriptKeys = new Dictionary<ushort, byte> {
            { 0x7E83, 0x66 },
            { 0x9383, 0x77 },
            { 0x4E83, 0x88 },
            { 0xBE82, 0x99 },
            { 0xA282, 0xAA },
            { 0xCE82, 0xBB },
            { 0xAD82, 0xCC },
            { 0xCD82, 0xDD },
            { 0xC282, 0xEE },
        };
    }
}
