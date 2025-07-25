using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

// [030704][Error] Kakoi ~Zetsubou no Shojo Kangokujima~
// [040227][Deko Pon!] Itsuka Furu Yuki
// [051216][Studio Ebisu] Nyuu wa SHOCK!! ~Pai o Torimodose!~
// [100126][Yaoyorozu-Kobo] Toraware no Seikishi Riisha

namespace GameRes.Formats.Yaneurao
{
    [Export(typeof(ArchiveFormat))]
    public class PackOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/yanepkDx"; } }
        public override string Description { get { return "Yaneurao resource archive"; } }
        public override uint     Signature { get { return 0x0C09140A; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public PackOpener ()
        {
            Signatures = new uint[] { 0x0C09140A, 0x656E6179 };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.View.AsciiEqual (0, "yane"))
            {
                if (!file.View.AsciiEqual (4, "pkDx"))
                    return null;
            }
            uint first_offset = file.View.ReadUInt32 (0x10C);
            if (first_offset < 0x118 || first_offset >= file.MaxOffset)
                return null;
            int count = (int)((first_offset - 0xC) / 0x10C);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = 0xC;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x100);
                index_offset += 0x100;
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.Offset       = file.View.ReadUInt32 (index_offset);
                entry.UnpackedSize = file.View.ReadUInt32 (index_offset+4);
                entry.Size         = file.View.ReadUInt32 (index_offset+8);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.IsPacked = entry.Size != entry.UnpackedSize;
                dir.Add (entry);
                index_offset += 0xC;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked)
                return input;
            return new LzssStream (input);
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class PackExOpener : PackOpener
    {
        public override string         Tag { get { return "DAT/yanepkEx"; } }
        public override string Description { get { return "Yaneurao resource archive"; } }
        public override uint     Signature { get { return 0x656E6179; } } // 'yanepkEx'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public PackExOpener ()
        {
            Signatures = new uint[] { 0x656E6179 };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (4, "pkEx"))
                return null;
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;
            uint index_offset = 0xC;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x20);
                index_offset += 0x20;
                var entry = FormatCatalog.Instance.Create<PackedEntry> (name);
                entry.Offset       = file.View.ReadUInt32 (index_offset);
                entry.UnpackedSize = file.View.ReadUInt32 (index_offset+4);
                entry.Size         = file.View.ReadUInt32 (index_offset+8);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                entry.IsPacked = entry.Size != entry.UnpackedSize;
                dir.Add (entry);
                index_offset += 0xC;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
