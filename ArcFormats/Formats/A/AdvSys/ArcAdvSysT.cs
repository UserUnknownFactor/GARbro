using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.AdvSys
{
    [Export(typeof(ArchiveFormat))]
    public class FpkOpener : ArchiveFormat
    {
        public override string         Tag { get { return "FPK/MFWY"; } }
        public override string Description { get { return "AdvSys_T engine resource archive"; } }
        public override uint     Signature { get { return 0x5957464D; } } // 'MFWY'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public FpkOpener ()
        {
            Extensions = new string[] { "fpk" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            uint data_offset = file.View.ReadUInt32 (8);
            if (data_offset < 0x10 + count * 0x20)
                return null;
            if (data_offset > file.View.Reserve (0, data_offset))
                return null;
            uint index_offset = 0x10;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = file.View.ReadString (index_offset, 0x18);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Size   = file.View.ReadUInt32 (index_offset+0x18);
                entry.Offset = file.View.ReadUInt32 (index_offset+0x1C);
                if (entry.Offset < data_offset || !entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_offset += 0x20;
            }
            return new ArcFile (file, this, dir);
        }
    }
}
