using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Formats.Eagls;
using GameRes.Utility;

// [121102][Tech Gian Archive] Hanafuda Market Totsugeki! Tonari no Kanban Musume

namespace GameRes.Formats.TechGian
{
    internal class RfilEntry : Entry
    {
        public int  EncryptionMethod;

        public new bool IsEncrypted {
            get { return EncryptionMethod == 1 || EncryptionMethod == 2 || EncryptionMethod == 4; }
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class BinOpener : ArchiveFormat
    {
        public override string         Tag { get { return "BIN/RFIL"; } }
        public override string Description { get { return "Tech Gian archive"; } }
        public override uint     Signature { get { return 0x4C494652; } } // 'RFIL'
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public override ArcFile TryOpen (ArcView file)
        {
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;
            bool is_encrypted = file.View.ReadInt32 (12) == 1234;
            long index_pos = 0x10;
            var buffer = new byte[0x40];
            var rnd = new CRuntimeRandomGenerator();
            rnd.SRand (0);
            var dir = new List<Entry> (count);
            for (int i = 0; i < count; ++i)
            {
                file.View.Read (index_pos, buffer, 0, 0x40);
                if (is_encrypted)
                    DecryptRand (buffer, 0, 0x40, rnd);
                var name = Binary.GetCString (buffer, 0, 0x30);
                var entry = Create<RfilEntry> (name);
                entry.Offset = buffer.ToUInt32 (0x34);
                entry.Size = buffer.ToUInt32 (0x38);
                entry.EncryptionMethod = buffer.ToInt32 (0x3C);
                if (!entry.CheckPlacement (file.MaxOffset))
                    return null;
                dir.Add (entry);
                index_pos += 0x40;
            }
            return new ArcFile (file, this, dir);
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var rent = (RfilEntry)entry;
            if (!rent.IsEncrypted)
                return base.OpenEntry (arc, entry);
            var data = arc.File.View.ReadBytes (rent.Offset, rent.Size);
            switch (rent.EncryptionMethod)
            {
            case 1:
                DecryptData (data, 0, data.Length);
                break;
            case 2:
                if (data.Length > 0)
                    DecryptData (data, 0, (data.Length - 1) / 100 + 1);
                break;
            case 4:
                DecryptData (data, 0, Math.Min (1024, data.Length));
                break;
            }
            return new BinMemoryStream (data, entry.Name);
        }

        internal static void DecryptData (byte[] data, int pos, int length)
        {
            while (length --> 0)
            {
                data[pos++] ^= 0x7F;
            }
        }

        internal static void DecryptRand (byte[] data, int pos, int length, IRandomGenerator rnd)
        {
            while (length --> 0)
            {
                data[pos++] ^= (byte)rnd.Rand();
            }
        }
    }
}
