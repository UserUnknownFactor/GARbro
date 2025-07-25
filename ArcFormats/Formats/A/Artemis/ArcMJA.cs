using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Artemis
{
    [Export(typeof(ArchiveFormat))]
    public class MjaOpener : ArchiveFormat
    {
        public override string         Tag { get { return "MJA"; } }
        public override string Description { get { return "Artemis engine animation"; } }
        public override uint     Signature { get { return 0x30414A4D; } } // 'MJA0'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;

            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            long offset = 8;
            var dir = new List<Entry>();
            int i = 0;
            while (offset < file.MaxOffset)
            {
                uint size = file.View.ReadUInt32 (offset);
                offset += 4;
                var entry = new Entry {
                    Name    = string.Format ("{0}#{1:D4}", base_name, i++),
                    Offset  = offset,
                    Size    = size
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                uint signature = file.View.ReadUInt32 (offset);
                var res = AutoEntry.DetectFileType (signature);
                entry.ChangeType (res);
                offset += size;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }
    }
}
