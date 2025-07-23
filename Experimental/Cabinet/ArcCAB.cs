using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using Microsoft.Deployment.Compression.Cab;

namespace GameRes.Formats.Microsoft
{
    internal class CabEntry : Entry
    {
        public readonly CabFileInfo  Info;

        public CabEntry (CabFileInfo file_info)
        {
            Info = file_info;
            Name = Info.Name;
            Type = FormatCatalog.Instance.GetTypeFromName (Info.Name);
            Size = (uint)Math.Min (Info.Length, uint.MaxValue);
            // offset is unknown and reported as '0' for all files.
            Offset = 0;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class CabOpener : ArchiveFormat
    {
        public override string         Tag { get { return "CAB"; } }
        public override string Description { get { return "Microsoft cabinet archive"; } }
        public override uint     Signature { get { return 0x4643534D; } } // 'MSCF'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (VFS.IsVirtual)
                throw new NotSupportedException ("Cabinet files inside archives not supported");
            var info = new CabInfo (file.Name);
            var dir = info.GetFiles().Select (f => new CabEntry (f) as Entry);
            return new ArcFile (file, this, dir.ToList());
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            return ((CabEntry)entry).Info.OpenRead();
        }
    }
}
