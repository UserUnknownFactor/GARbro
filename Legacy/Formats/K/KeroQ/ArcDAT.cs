using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.KeroQ
{
    [Export(typeof(ArchiveFormat))]
    public class PacOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/PAC"; } }
        public override string Description { get { return "KeroQ resource archive"; } }
        public override uint     Signature { get { return 0x43415089; } } // '\x89PAC'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            var pac_name = Path.GetFileNameWithoutExtension (file.Name);
            int pac_num;
            if (!Int32.TryParse (pac_name, out pac_num))
                return null;
            var hdr_name = string.Format ("{0:D3}.dat", pac_num - 1);
            hdr_name = VFS.ChangeFileName (file.Name, hdr_name);
            if (!VFS.FileExists (hdr_name))
                return null;
            using (var index = VFS.OpenBinaryStream (hdr_name))
            {
                var header = index.ReadHeader (8);
                if (!header.AsciiEqual ("\x89HDR"))
                    return null;
                if (header.ToInt32 (4) != count)
                    return null;
                var dir = new List<Entry> (count);
                for (int i = 0; i < count; ++i)
                {
                    var name = index.ReadCString (0x10);
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Size = index.ReadUInt32();
                    entry.Offset = index.ReadUInt32();
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                }
                return new ArcFile (file, this, dir);
            }
        }
    }
}
