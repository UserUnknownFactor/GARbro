using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text.RegularExpressions;

namespace GameRes.Formats.Ucom
{
    [Export(typeof(ArchiveFormat))]
    public class DataOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DATA/UCOM"; } }
        public override string Description { get { return "For/Ucom scripts archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public DataOpener ()
        {
            Extensions = new string[] { "" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "PF"))
                return null;
            var base_name = Path.GetFileName (file.Name);
            if (!base_name.Equals ("data02", StringComparison.InvariantCultureIgnoreCase))
                return null;
            var index_name = VFS.CombinePath (VFS.GetDirectoryName (file.Name), "data01");
            if (!VFS.FileExists (index_name))
                return null;
            using (var index = VFS.OpenView (index_name))
            {
                if (!index.View.AsciiEqual (0, "IF"))
                    return null;
                int count = index.View.ReadInt16 (2);
                if (!IsSaneCount (count) || 4 + 0x18 * count > index.MaxOffset)
                    return null;

                uint index_offset = 4;
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    var name = index.View.ReadString (index_offset, 0x10);
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Offset = index.View.ReadUInt32 (index_offset+0x10);
                    entry.Size   = index.View.ReadUInt32 (index_offset+0x14);
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    index_offset += 0x18;
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            for (int i = 0; i < data.Length; ++i)
            {
                if ((i % 5) != 0)
                    data[i] ^= 0x45;
            }
            return new BinMemoryStream (data, entry.Name);
        }
    }
}
