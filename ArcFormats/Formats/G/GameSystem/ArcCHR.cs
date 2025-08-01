using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;

namespace GameRes.Formats.GameSystem
{
    [Export(typeof(ArchiveFormat))]
    public class ChrOpener : ArchiveFormat
    {
        public override string         Tag { get { return "CHR/GAMESYSTEM"; } }
        public override string Description { get { return "'Game System' character frames"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return false; } }
        public override bool      CanWrite { get { return false; } }

        static readonly Lazy<ImageFormat> s_ChrFormat = new Lazy<ImageFormat> (() => ImageFormat.FindByTag ("CHR"));

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.Name.HasExtension (".CHR")
                || file.View.ReadUInt32 (0) != file.MaxOffset)
                return null;
            using (var input = file.CreateStream())
            {
                var info = s_ChrFormat.Value.ReadMetaData (input) as ChrMetaData;
                if (null == info)
                    return null;
                input.Position = info.RgbSize;
                uint overlay_size = input.ReadUInt32();
                if (0 == overlay_size)
                    return null;
                input.ReadInt32();
                int count = input.ReadInt32();
                if (!IsSaneCount (count))
                    return null;
                int x = input.ReadInt16();
                int y = input.ReadInt16();
                int w = input.ReadInt16();
                int h = input.ReadInt16() * count;
                var frame_info = new ImageMetaData
                {
                    Width = (uint)w, Height = (uint)h, OffsetX = x, OffsetY = y, BPP = 32
                };

                var base_name = Path.GetFileNameWithoutExtension (file.Name);
                var dir = new List<Entry> (2);
                var entry = new ChrEntry
                {
                    Name = string.Format ("{0}#00", base_name),
                    Offset = 0,
                    Size = (uint)info.RgbSize,
                    Info = info,
                };
                dir.Add (entry);
                entry = new ChrEntry
                {
                    Name = string.Format ("{0}#01", base_name),
                    Offset = info.RgbSize+4,
                    Size = overlay_size,
                    Info = frame_info,
                };
                dir.Add (entry);
                return new ArcFile (file, this, dir);
            }
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var cent = (ChrEntry)entry;
            var input = arc.File.CreateStream (entry.Offset, entry.Size);
            if (cent.Info is ChrMetaData)
                return new ChrDecoder (input, cent.Info as ChrMetaData);
            else
                return new ChrFrameDecoder (input, cent.Info);
        }
    }

    internal class ChrEntry : Entry
    {
        public override string Type { get { return "image"; } }

        public ImageMetaData    Info;
    }

    internal class ChrDecoder : ChrReader
    {
        public ChrDecoder (IBinaryStream input, ChrMetaData info) : base (input, info)
        { }

        protected override ImageData GetImageData ()
        {
            var pixels = UnpackBaseline();
            return ImageData.CreateFlipped (Info, PixelFormats.Bgra32, null, pixels, Stride);
        }
    }

    internal class ChrFrameDecoder : BinaryImageDecoder
    {
        public ChrFrameDecoder (IBinaryStream input, ImageMetaData info) : base (input, info)
        { }

        protected override ImageData GetImageData ()
        {
            m_input.Position = 0x10;
            int stride = (int)Info.Width * 4;
            var pixels = new byte[stride * (int)Info.Height];
            m_input.Read (pixels, 0, pixels.Length);
            for (int i = 3; i < pixels.Length; i += 4)
                pixels[i] = (byte)(pixels[i] * 0xFF / 0x80);
            return ImageData.CreateFlipped (Info, PixelFormats.Bgra32, null, pixels, stride);
        }
    }
}
