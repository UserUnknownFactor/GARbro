using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;

namespace GameRes.Formats.BlackRainbow
{
    [Export(typeof(ArchiveFormat))]
    public class PackOpener : ArchiveFormat
    {
        public override string         Tag { get { return "PACK/DX"; } }
        public override string Description { get { return "DX engine resource archive"; } }
        public override uint     Signature { get { return 0x4B434150; } } // 'PACK'
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public PackOpener ()
        {
            Extensions = new string[] { "pak" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            uint index_length = file.View.ReadUInt32 (4);
            if (index_length >= file.MaxOffset)
                return null;
            using (var input = file.CreateStream (0, index_length))
            {
                var reader = new IndexReader (input, file.MaxOffset);
                if (!reader.ReadDir ("", 8, index_length) || 0 == reader.Dir.Count)
                    return null;
                return new ArcFile (file, this, reader.Dir);
            }
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            if (!entry.Name.HasExtension (".hse"))
                return base.OpenEntry (arc, entry);
            var data = arc.File.View.ReadBytes (entry.Offset, entry.Size);
            for (int i = 0; i < data.Length; ++i)
                data[i] = (byte)-data[i];
            return new BinMemoryStream (data, entry.Name);
        }

        internal class IndexReader
        {
            IBinaryStream       m_index;
            long                m_max_offset;
            List<Entry>         m_dir;

            public List<Entry> Dir { get { return m_dir; } }

            public IndexReader (IBinaryStream input, long max_offset)
            {
                m_index = input;
                m_max_offset = max_offset;
                m_dir = new List<Entry>();
            }

            public bool ReadDir (string root, uint dir_offset, uint base_offset)
            {
                m_index.Position = dir_offset;
                int dir_count = m_index.ReadInt32();
                dir_offset += 4;
                for (int i = 0; i < dir_count; ++i)
                {
                    var  name        = m_index.ReadCString (0x20);
                    uint offset      = m_index.ReadUInt32();
                    uint data_offset = m_index.ReadUInt32();
                    if (offset <= dir_offset || offset > m_index.Length || data_offset > m_max_offset)
                        return false;
                    if (!ReadDir (Path.Combine (root, name), offset, data_offset))
                        return false;
                    dir_offset += 0x28;
                    m_index.Position = dir_offset;
                }
                int count = m_index.ReadInt32();
                if (0 == count)
                    return true;
                if (!IsSaneCount (count))
                    return false;
                for (int i = 0; i < count; ++i)
                {
                    var name = m_index.ReadCString (0x20);
                    name = Path.Combine (root, name);
                    var entry = FormatCatalog.Instance.Create<Entry> (name);
                    entry.Offset = m_index.ReadUInt32() + base_offset;
                    entry.Size   = m_index.ReadUInt32();
                    if (!entry.CheckPlacement (m_max_offset))
                        return false;
                    if (name.HasExtension (".hse"))
                        entry.Type = "image";
                    m_dir.Add (entry);
                }
                return true;
            }
        }
    }
}
