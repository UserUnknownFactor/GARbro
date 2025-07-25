using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text.RegularExpressions;

// [010727][Marron] Cosmos no Sora ni

namespace GameRes.Formats.Marron
{
    internal class CpnArchive : ArcFile
    {
        public readonly byte Key;

        public CpnArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, byte key)
            : base (arc, impl, dir)
        {
            Key = key;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class DatOpener : ArchiveFormat
    {
        public override string         Tag { get { return "DAT/CPN"; } }
        public override string Description { get { return "Marron resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        static readonly Regex CpnEntryRe = new Regex (@"#(?:\./)?(?<name>[^$]+)\$(?<offset>\d+)\*(?<size>\d+)\+");

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".dat"))
                return null;
            var cpn_name = Path.ChangeExtension (file.Name, ".cpn");
            if (!VFS.FileExists (cpn_name))
                return null;
            byte key;
            string cpn_index;
            using (var cpn = VFS.OpenView (cpn_name))
            {
                key = cpn.View.ReadByte (0);
                var cpn_data = cpn.View.ReadBytes (1, (uint)(cpn.MaxOffset - 1));
                for (int i = 0; i < cpn_data.Length; ++i)
                    cpn_data[i] ^= key;
                cpn_index = Encodings.cp932.GetString (cpn_data);
            }
            int idx = cpn_index.IndexOf ('#', 1);
            if (idx <= 1)
                return null;
            var data_name = cpn_index.Substring (1, idx-1);
            if (!VFS.IsPathEqualsToFileName (file.Name, data_name))
                return null;
            var dir = new List<Entry>();
            var match = CpnEntryRe.Match (cpn_index, idx);
            while (match.Success)
            {
                var name = match.Groups["name"].Value;
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = UInt32.Parse (match.Groups["offset"].Value);
                entry.Size   = UInt32.Parse (match.Groups["size"].Value);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                match = match.NextMatch();
            }
            if (0 == dir.Count)
                return null;
            return new CpnArchive (file, this, dir, key);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var carc = arc as CpnArchive;
            if (null == carc)
                return input;
            return new XoredStream (input, carc.Key);
        }
    }
}
