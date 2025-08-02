using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.Unknown
{
    [Export(typeof(ArchiveFormat))]
    public class PakOpener : ArchiveFormat
    {
        public override string         Tag => "ARC";
        public override string Description => "Unknown engine resource archive";
        public override uint     Signature => 0;
        public override bool  IsHierarchic => false;
        public override bool      CanWrite => false;

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt16 (0);
            if (!IsSaneCount (count))
                return null;

            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                string name = "";
                var entry = Create<Entry> (name);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            return base.OpenEntry (arc, entry);
        }
    }
}
