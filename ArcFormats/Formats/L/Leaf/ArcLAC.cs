using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

namespace GameRes.Formats.Leaf
{
    [Export(typeof(ArchiveFormat))]
    public class LacOpener : ArchiveFormat
    {
        public override string         Tag { get { return "LAC"; } }
        public override string Description { get { return "Leaf resource archive"; } }
        public override uint     Signature { get { return 0x43414C; } } // 'LAC'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;

            uint index_offset = 8;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x3E);
                if (string.IsNullOrEmpty (name))
                    return null;
                index_offset += 0x3E;
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.IsPacked = 0 != file.View.ReadByte (index_offset);
                index_offset += 0xE;
                entry.Size = file.View.ReadUInt32 (index_offset);
                entry.UnpackedSize = file.View.ReadUInt32 (index_offset+4);
                entry.Offset = file.View.ReadInt64 (index_offset+0xC);
                index_offset += 0x2C;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = base.OpenEntry (arc, entry);
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return input;
            var lzs = new LzssStream (input);
            lzs.Config.FrameFill = 0x20;
            return lzs;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK/LAC"; } }
        public override string Description { get { return "Leaf resource archive"; } }
        public override uint     Signature { get { return 0x43414C; } } // 'LAC'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public PakOpener ()
        {
            Extensions = new string[] { "pak" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;

            uint index_offset = 8;
            var dir = new List<Entry> (count);
            var name_buf = new byte[0x20];
            for (int i = 0; i < count; ++i)
            {
                file.View.Read (index_offset, name_buf, 0, 0x20);
                index_offset += 0x20;
                int l;
                for (l = 0; l < 0x1F && name_buf[l] != 0; ++l)
                {
                    name_buf[l] ^= 0xFF;
                }
                if (0 == l)
                    return null;
                var name = Encodings.cp932.GetString (name_buf, 0, l);
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.IsPacked = 0 != name_buf[0x1F];
                entry.Size = file.View.ReadUInt32 (index_offset);
                entry.Offset = file.View.ReadUInt32 (index_offset+4);
                index_offset += 8;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked || entry.Size <= 4)
                return base.OpenEntry (arc, entry);
            if (0 == pent.UnpackedSize)
            {
                uint size = arc.File.View.ReadUInt32 (entry.Offset);
                if (size == pent.Size)
                {
                    pent.Offset += 4;
                    pent.Size -= 4;
                }
                pent.UnpackedSize = arc.File.View.ReadUInt32 (entry.Offset);
                pent.Offset += 4;
                pent.Size -= 4;
                if (0 == pent.UnpackedSize)
                    ++pent.UnpackedSize;
            }
            Stream input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new LzssStream (input);
        }
    }
}
