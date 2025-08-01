using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Compression;
using GameRes.Utility;

// [051028][ALTERNA] ShiroKuro ~Shiroi Kokoro to Kuroi Tsurugi~

namespace GameRes.Formats.Alterna
{
    [Export(typeof(ArchiveFormat))]
    public class BinOpener : ArchiveFormat
    {
        public override string         Tag { get { return "BIN/ARC1"; } }
        public override string Description { get { return "Alterna resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public BinOpener ()
        {
            ContainedFormats = new[] { "BMP", "OGG", "SCR" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (file.Name.HasExtension (".lst"))
                return null;
            var lst_name = Path.ChangeExtension (file.Name, ".lst");
            if (!VFS.FileExists (lst_name))
                return null;
            using (var lst = VFS.OpenView (lst_name))
            {
                if (!lst.View.AsciiEqual (0, "ARC1.00"))
                    return null;
                int count = lst.View.ReadInt32 (8);
                if (!IsSaneCount (count))
                    return null;
                uint lst_pos = 0x10;
                var name_buffer = new byte[0x20];
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    lst.View.Read (lst_pos+0x10, name_buffer, 0, 0x20);
                    for (int j = 0; j < 0x20 && name_buffer[j] != 0; ++j)
                    {
                        name_buffer[j] ^= 0x80;
                    }
                    var name = Binary.GetCString (name_buffer, 0);
                    var entry = Create<PackedEntry> (name);
                    entry.Size   = lst.View.ReadUInt32 (lst_pos + 8);
                    entry.Offset = lst.View.ReadUInt32 (lst_pos + 0xC);
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    entry.UnpackedSize = lst.View.ReadUInt32 (lst_pos + 4);
                    entry.IsPacked = lst.View.ReadInt32 (lst_pos) != 0;
                    dir.Add (entry);
                    lst_pos += 0x30;
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var pent = entry as PackedEntry;
            if (null == pent || !pent.IsPacked || !arc.File.View.AsciiEqual (entry.Offset, "LZSS"))
                return base.OpenEntry (arc, entry);
            var input = arc.File.CreateStream (entry.Offset+8, entry.Size-8);
            var lzss = new LzssStream (input);
            lzss.Config.FrameInitPos = 0xFF0;
            return lzss;
        }
    }

    [Export(typeof(ResourceAlias))]
    [ExportMetadata("Extension", "STR")]
    [ExportMetadata("Target", "SCR")]
    public class StrFormat : ResourceAlias { }
}
