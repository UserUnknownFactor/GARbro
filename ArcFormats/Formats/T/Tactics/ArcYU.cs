using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using GameRes.Utility;

namespace GameRes.Formats.Tactics
{
    internal class YuEntry : Entry
    {
        public byte ContentType;
    }

    [Export(typeof(ArchiveFormat))]
    public class YuOpener : ArchiveFormat
    {
        public override string         Tag { get { return "ARC/Tactics/0"; } }
        public override string Description { get { return "Tactics resource archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        const byte DefaultKey = 0x55;

        static readonly string[] IndexExtensions = new string[] { ".dll" };
        static readonly string[] KnownTypes = new string[] { "bmp", "jpg" };

        public override ArcFile TryOpen (ArcView file)
        {
            string lst_name = IndexExtensions.Select (ext => file.Name + ext)
                .FirstOrDefault (name => VFS.FileExists (name));
            if (null == lst_name)
                return null;
            using (var lst = VFS.OpenBinaryStream (lst_name))
            {
                const int name_length = 0x41;
                var dir = new List<Entry>();
                var name_buffer = new byte[name_length];
                while (lst.Read (name_buffer, 0, name_length) == name_length)
                {
                    int name_end;
                    for (name_end = 0; name_end < name_length; ++name_end)
                    {
                        if (0 == name_buffer[name_end])
                            break;
                        name_buffer[name_end] ^= DefaultKey;
                    }
                    var name = Binary.GetCString (name_buffer, 0, name_end);
                    uint offset = lst.ReadUInt32();
                    if (offset > file.MaxOffset)
                        return null;
                    byte type = lst.ReadUInt8();
                    if (type < KnownTypes.Length)
                        name = Path.ChangeExtension (name, KnownTypes[type]);
                    var entry = FormatCatalog.Instance.Create<YuEntry> (name);
                    entry.Offset = offset;
                    entry.ContentType = type;
                    dir.Add (entry);
                }
                if (0 == dir.Count)
                    return null;
                for (int i = 0; i < dir.Count; ++i)
                {
                    long next_offset = i + 1 == dir.Count ? file.MaxOffset : dir[i+1].Offset;
                    dir[i].Size = (uint)(next_offset - dir[i].Offset);
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            var yent = entry as YuEntry;
            if (null == yent || yent.ContentType != 3)
                return input;
            return new XoredStream (input, DefaultKey);
        }
    }
}
