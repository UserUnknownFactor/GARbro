using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.SquadraD
{
    internal class PlaEntry : PackedEntry
    {
        public  int Id;
        public  int n1;
        public uint SampleRate;
        public  int Channels;
        public byte n2;
        public byte n3;
        public int[] Data;
    }

    [Export(typeof(ArchiveFormat))]
    public class PlaOpener : ArchiveFormat
    {
        public override string         Tag => "PLA";
        public override string Description => "Squadra D audio archive";
        public override uint     Signature => 0x2E616C50; // 'Pla.'
        public override bool  IsHierarchic => false;
        public override bool      CanWrite => false;

        public override ArcFile TryOpen (ArcView file)
        {
            uint arc_size = file.View.ReadUInt32 (4);
            if (arc_size != file.MaxOffset || file.View.ReadUInt32 (0x10) != 2)
                return null;
            uint check = (arc_size & 0xD5555555u) << 1 | arc_size & 0xAAAAAAAAu;
            if (check != file.View.ReadUInt32 (8))
                return null;
            int count = file.View.ReadUInt16 (0xE);
            if (!IsSaneCount (count))
                return null;

            var dir = new List<Entry> (count);
            using (var index = file.CreateStream())
            {
                index.Position = 0x14;
                for (int i = 0; i < count; ++i)
                {
                    var entry = new PlaEntry {
                        Id = index.ReadInt32()
                    };
                    entry.Name = entry.Id.ToString ("D5");
                    dir.Add (entry);
                }
                foreach (PlaEntry entry in dir)
                {
                    entry.n1 = index.ReadInt32();
                    entry.SampleRate = index.ReadUInt32();
                    entry.Channels = index.ReadInt32();
                    entry.n2 = index.ReadUInt8();
                    entry.n3 = index.ReadUInt8();
                    index.ReadInt16();
                }
                foreach (PlaEntry entry in dir)
                {
                    entry.Offset = index.ReadUInt32();
                }
                foreach (PlaEntry entry in dir)
                {
                    int n = entry.Channels * 2;
                    entry.Data = new int[n];
                    for (int j = 0; j < n; ++j)
                        entry.Data[j] = index.ReadInt32();
                }
            }
            long last_offset = file.MaxOffset;
            for (int i = dir.Count - 1; i >= 0; --i)
            {
                dir[i].Size = (uint)(last_offset - dir[i].Offset);
                last_offset = dir[i].Offset;
            }
            return new ArcFile (file, this, dir);
        }

        /*
        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            return base.OpenEntry (arc, entry);
        }
        */
    }
}
