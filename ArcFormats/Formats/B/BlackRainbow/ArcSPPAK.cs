using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.BlackRainbow
{
    internal class SpArchive : ArcFile
    {
        public readonly byte Key;

        public SpArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, byte key)
            : base (arc, impl, dir)
        {
            Key = key;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class SpPakOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PAK/SP"; } }
        public override string Description { get { return "BlackRainbow script archive"; } }
        public override uint     Signature { get { return 0x69695669; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public SpPakOpener ()
        {
            Signatures = KnownSchemes.Keys;
        }

        static readonly Dictionary<uint, byte> KnownSchemes = new Dictionary<uint, byte>
        {
            { 0x69695669, 0x07 }, // Kannagi
            { 0x8492E36F, 0x9C }, // From M
        };

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (4);
            if (!IsSaneCount (count))
                return null;
            byte key = KnownSchemes[file.View.ReadUInt32 (0)];

            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            uint index_offset = 8;
            long base_offset = index_offset + 4 * count;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var name = string.Format ("{0}#{1:D4}", base_name, i);
                var entry = FormatCatalog.Instance.Create<Entry> (name);
                entry.Offset = base_offset + file.View.ReadUInt32 (index_offset);
                index_offset += 4;
                dir.Add (entry);
            }
            for (int i = 1; i < dir.Count; ++i)
            {
                dir[i-1].Size = (uint)(dir[i].Offset - dir[i-1].Offset);
            }
            var last_entry = dir[dir.Count-1];
            last_entry.Size = (uint)(file.MaxOffset - last_entry.Offset);
            return new SpArchive (file, this, dir, key);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var sp_arc = (SpArchive)arc;
            var key = sp_arc.Key;
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            for (int i = 0; i < data.Length; ++i)
            {
                data[i] = Binary.RotByteR ((byte)(data[i] ^ key), 2);
            }
            return new BinMemoryStream (data, entry.Name);
        }
    }
}
