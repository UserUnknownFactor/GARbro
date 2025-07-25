﻿using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;

namespace GameRes.Formats.CatSystem
{
    [Export(typeof(ArchiveFormat))]
    public class ZtOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ZT/PACK"; } }
        public override string Description { get { return "CatSystem2 pack file"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public ZtOpener ()
        {
            Extensions = new string[] { "zt" };
        }

        struct ZtSubdirectory
		{
            public string Name;
            public long Offset;
		}

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.MaxOffset < 0x11C)
                return null;
            if (0x110 > file.View.ReadUInt32 (8) || 1 < file.View.ReadUInt32 (0xC))
                return null; // First entry is too small, or not a file or folder
            var dir = new List<Entry> ();
            var subdirs = new Queue<ZtSubdirectory> ();
            const string sep = "\\";
            string parent_name = "";
            long offset = 0;
            uint offset_next;
            do
            {
                offset_next     = file.View.ReadUInt32 (offset);
                uint entry_size = file.View.ReadUInt32 (offset+8);
                if (0 != offset_next && 0xC + entry_size > offset_next)
                    return null;
                uint attributes = file.View.ReadUInt32 (offset+0xC);
                string name     = file.View.ReadString (offset+0x10, 0x104);
                if (1 < attributes || 0 == name.Length)
                    return null;
                if (0 == attributes)
                {
                    var entry = FormatCatalog.Instance.Create<PackedEntry> (parent_name + name);
                    uint packed_size = file.View.ReadUInt32 (offset+0x114);
                    if (0x110 + packed_size != entry_size)
                        return null;
                    entry.Offset = offset + 0x11C;
                    entry.Size = packed_size;
                    entry.UnpackedSize = file.View.ReadUInt32 (offset+0x118);
                    entry.IsPacked = true;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                else if (0 != file.View.ReadUInt32 (offset+4))
                {
                    subdirs.Enqueue (new ZtSubdirectory { Name = parent_name + name + sep, Offset = offset });
                }

                if (0 == offset_next && 0 != subdirs.Count)
                {   // No more entries in current directory, go to next subdirectory
                    var subdir = subdirs.Dequeue ();
                    parent_name = subdir.Name;
                    offset      = subdir.Offset;
                    offset_next = file.View.ReadUInt32 (offset+4); // offset_child
                    if (0xC + file.View.ReadUInt32 (offset+8) > offset_next)
                        return null;
                }
                offset += offset_next;
            }
            while (0 != offset_next);

            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pentry = (PackedEntry)entry;
            if (0 == pentry.UnpackedSize)
                return Stream.Null;
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new ZLibStream (input, CompressionMode.Decompress);
        }
    }
}
