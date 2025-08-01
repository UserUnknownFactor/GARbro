using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

namespace GameRes.Formats.Youkai
{
    [Export(typeof(ArchiveFormat))]
    public class GrpDatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/YOUKAI/1"; } }
        public override string Description { get { return "Youkai Tamanokoshi resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".dat"))
                return null;
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count) || 0 != file.View.ReadInt32 (4))
                return null;
            uint index_offset = 0x20;
            uint data_offset = index_offset + (uint)count * 0x110;
            if (data_offset >= file.MaxOffset)
                return null;

            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x100);
                if (0 == name.Length)
                    return null;
                index_offset += 0x100;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Size = file.View.ReadUInt32 (index_offset);
                entry.Offset = file.View.ReadUInt32 (index_offset+4);
                if (entry.Offset < data_offset || !entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x10;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (!arc.File.View.AsciiEqual (entry.Offset, "ACMPRS03"))
                return base.OpenEntry (arc, entry);
            uint packed_size = arc.File.View.ReadUInt32 (entry.Offset+0x14);
            var input = arc.File.CreateStream (entry.Offset+0x24, packed_size);
            return new LzssStream (input);
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class SoundDatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/YOUKAI/2"; } }
        public override string Description { get { return "Youkai Tamanokoshi audio archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".dat"))
                return null;
            int count = file.View.ReadInt32 (0);
            if (!IsSaneCount (count))
                return null;
            uint current_offset = 4;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                if (current_offset >= file.MaxOffset-0x104)
                    return null;
                var name = file.View.ReadString (current_offset, 0x100);
                if (0 == name.Length)
                    return null;
                uint size = file.View.ReadUInt32 (current_offset+0x100);
                current_offset += 0x104;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = current_offset;
                entry.Size = size;
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                current_offset += size;
            }
            if (current_offset != file.MaxOffset)
                return null;
            return new ArcFile (file, this, dir);
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class VoiceDatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/YOUKAI/3"; } }
        public override string Description { get { return "Youkai Tamanokoshi audio archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".dat"))
                return null;
            uint data_offset = file.View.ReadUInt32 (0);
            int count = file.View.ReadInt32 (4);
            uint index_offset = 8;
            uint index_size = (uint)count * 0x108;
            if (!IsSaneCount (count) || index_offset + index_size > data_offset)
                return null;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x100);
                if (0 == name.Length)
                    return null;
                index_offset += 0x100;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Size = file.View.ReadUInt32 (index_offset);
                entry.Offset = file.View.ReadUInt32 (index_offset+4);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 8;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
