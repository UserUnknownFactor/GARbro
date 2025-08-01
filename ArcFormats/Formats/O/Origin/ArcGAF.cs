using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.Origin
{
    internal class GafArchive : ArcFile
    {
        public readonly ImageMetaData  Info;

        public GafArchive (ArcView arc, ArchiveFormat impl, ICollection<Entry> dir, ImageMetaData info)
            : base (arc, impl, dir)
        {
            Info = info;
        }
    }

    [Export(typeof(ArchiveFormat))]
    public class GafOpener : ArchiveFormat
    {
        public override string         Tag { get { return "GAF/ORIGIN"; } }
        public override string Description { get { return "origin engine bitmap archive"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        public GafOpener ()
        {
            Extensions = new string[] { "" };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!VFS.IsPathEqualsToFileName (file.Name, "gaf"))
                return null;
            int count = file.View.ReadInt32 (8);
            if (!IsSaneCount (count))
                return null;
            uint width  = file.View.ReadUInt32 (0);
            uint height = file.View.ReadUInt32 (4);
            if (width == 0 || width > 0x4000 || height == 0 || height > 0x4000)
                return null;
            uint offset = 0xC;
            var base_name = Path.GetFileNameWithoutExtension (file.Name);
            var dir = new List<Entry> (count);
            uint image_size = width * height;
            for (int i = 0; i < count; ++i)
            {
                var entry = new Entry {
                    Name = string.Format ("{0}#{1:D4}", base_name, i),
                    Type = "image",
                    Offset = offset,
                };
                if (i + 1 < count)
                {
                    uint unpacked = 0;
                    while (unpacked < image_size && offset < file.MaxOffset)
                    {
                        unpacked += file.View.ReadByte (offset+1);
                        offset += 2;
                    }
                }
                else
                {
                    offset = (uint)file.MaxOffset;
                }
                entry.Size = (uint)(offset - entry.Offset);
                dir.Add (entry);
            }
            var info = new ImageMetaData { Width = width, Height = height, BPP = 8 };
            return new GafArchive (file, this, dir, info);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var garc = (GafArchive)arc;
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            return new GafImageDecoder (input, garc.Info);
        }
    }

    internal class GafImageDecoder : BinaryImageDecoder
    {
        public GafImageDecoder (IBinaryStream input, ImageMetaData info) : base (input, info)
        {
        }

        protected override ImageData GetImageData ()
        {
            var pixels = new byte[Info.iWidth * Info.iHeight];
            UnpackRle (pixels);
            return ImageData.Create (Info, PixelFormats.Gray8, null, pixels, Info.iWidth);
        }

        void UnpackRle (byte[] output)
        {
            int dst = 0;
            while (dst < output.Length)
            {
                byte symbol = m_input.ReadUInt8();
                int count = Math.Min (m_input.ReadUInt8(), output.Length - dst);
                while (count --> 0)
                    output[dst++] = (byte)symbol;
            }
        }
    }
}
