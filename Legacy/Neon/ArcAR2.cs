using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

// [990527][Neon] Onegai! Maid☆Roid

namespace GameRes.Formats.Neon
{
    [Export(typeof(ArchiveFormat))]
    public class Ar2Opener : ArchiveFormat
    {
        public override string         Tag => "AR2/NEON";
        public override string Description => "Neon resource archive";
        public override uint     Signature => 0;
        public override bool  IsHierarchic => false;
        public override bool      CanWrite => false;

        const byte DefaultKey = 0x55;

        public override ArcFile TryOpen (ArcView file)
        {
            uint uKey = DefaultKey | DefaultKey << 16;
            uKey |= uKey << 8;
            if (file.MaxOffset <= 0x10
                || file.View.ReadUInt32 (8) != uKey
                || file.View.ReadUInt32 (0) != file.View.ReadUInt32 (4)
                || (file.View.ReadUInt32 (0xC) ^ uKey) > 0x100)
                return null;
            using (var stream = file.CreateStream())
            using (var input = new XoredStream (stream, DefaultKey))
            {
                var buffer = new byte[0x100];
                var dir = new List<Entry>();
                while (0x10 == input.Read (buffer, 0, 0x10))
                {
                    uint size = buffer.ToUInt32 (0);
//                    uint orig_size = buffer.ToUInt32 (4); // original size?
//                    uint extra = buffer.ToUInt32 (8); // header size/compression?
                    int name_length = buffer.ToInt32 (0xC);
                    if (0 == size && 0 == name_length)
                        continue;
                    if (name_length <= 0 || name_length > buffer.Length)
                        return null;
                    input.Read (buffer, 0, name_length);
                    var name = Encodings.cp932.GetString (buffer, 0, name_length);
                    var entry = Create<Entry> (name);
                    entry.Offset = input.Position;
                    entry.Size = size;
                    if (!entry.CheckPlacement (file.MaxOffset))
                        return null;
                    dir.Add (entry);
                    input.Seek (size, SeekOrigin.Current);
                }
                return new ArcFile (file, this, dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new XoredStream (input, DefaultKey);
        }
    }
}
