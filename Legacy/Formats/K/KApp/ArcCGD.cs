using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

// [001006][spiel] Koimusubi

namespace GameRes.Formats.KApp
{
    internal class CgdEntry : Entry
    {
        public CgdMetaData Info;
    }

    [Export(typeof(ArchiveFormat))]
    public class CgdOpener : ArchiveFormat
    {
        public override string         Tag { get { return "CGD/SPIEL"; } }
        public override string Description { get { return "Spiel image collection"; } }
        public override uint     Signature { get { return 0x65697073; } } // 'spiel100'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "spiel100"))
                return null;
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;

            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            uint index_pos = 0x10;
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                var entry = new CgdEntry {
                    Name   = string.Format ("{0}#{1:D4}", base_name, i),
                    Type   = "image",
                    Offset = file.View.ReadUInt32 (index_pos),
                    Size   = file.View.ReadUInt32 (index_pos+4),
                };
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                var info = entry.Info = new CgdMetaData {
                    Width  = file.View.ReadUInt16 (index_pos+8),
                    Height = file.View.ReadUInt16 (index_pos+0xA),
                    BPP    = file.View.ReadByte (index_pos+0xE),
                    DataOffset = 0,
                    Compression = file.View.ReadByte (index_pos+0xF),
                    RgbOrder = false,
                };
                info.UnpackedSize = info.iWidth * info.iHeight * info.BPP / 8;
                dir.Add (entry);
                index_pos += 0x10;
            }
            return new ArcFile (file, this, dir);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var cgent = entry as CgdEntry;
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (null == cgent)
                return ImageFormatDecoder.Create (input);
            return new CgdDecoder (input, cgent.Info);
        }
    }
}
