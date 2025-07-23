//! \file       ArcSWF.cs
//! \date       2025 Jul 21
//! \brief      Shockwave Flash presentation parser.
//
// Copyright (C) 2018 by morkt
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to
// deal in the Software without restriction, including without limitation the
// rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
// sell copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
// IN THE SOFTWARE.
//

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Compression;

namespace GameRes.Formats.Macromedia
{
    internal class SwfEntry : Entry
    {
        public SwfChunk     Chunk;
        public string       Path { get; set; }
        public List<Entry>  Children { get; set; }
    }

    internal class SwfSoundEntry : SwfEntry
    {
        public readonly List<SwfChunk>  SoundStream = new List<SwfChunk>();
    }

    internal class SwfSpriteEntry : SwfEntry
    {
        public List<SwfChunk> SpriteChunks { get; set; } = new List<SwfChunk>();
    }

    [Export(typeof(ArchiveFormat))]
    public class SwfOpener : ArchiveFormat
    {
        public override string         Tag { get { return "SWF"; } }
        public override string Description { get { return "Shockwave Flash presentation"; } }
        public override uint     Signature { get { return 0; } }
        public override bool  IsHierarchic { get { return true; } }
        public override bool      CanWrite { get { return false; } }

        public SwfOpener ()
        {
            Signatures = new uint[] { 0x08535743, 0x08535746, 0 };
        }

        public override ArcFile TryOpen (ArcView file)
        {
            if (!file.View.AsciiEqual (0, "CWS") && !file.View.AsciiEqual (0, "FWS"))
                return null;

            bool is_compressed = file.View.ReadByte (0) == 'C';
            int version = file.View.ReadByte (3);
            using (var reader = new SwfReader (file.CreateStream(), version, is_compressed))
            {
                var chunks = reader.Parse();
                var base_name = Path.GetFileNameWithoutExtension (file.Name);

                // Build hierarchical structure
                var root_entries = new List<Entry>();
                var sprite_stack = new Stack<SwfSpriteEntry>();
                SwfSoundEntry current_stream = null;
                var resource_groups = new Dictionary<string, List<Entry>>();

                foreach (var chunk in chunks)
                {
                    if (chunk.Type == Types.DefineSprite)
                    {
                        var sprite_entry = new SwfSpriteEntry
                        {
                            Name = string.Format ("Sprite_{0:D5}", chunk.Id),
                            Type = "sprite",
                            Chunk = chunk,
                            Offset = 0,
                            Size = (uint)chunk.Length,
                            Children = new List<Entry>()
                        };

                        if (sprite_stack.Count > 0)
                        {
                            sprite_stack.Peek().Children.Add(sprite_entry);
                        }
                        else
                        {
                            AddToResourceGroup(resource_groups, "Sprites", sprite_entry);
                        }
                        sprite_stack.Push(sprite_entry);
                    }
                    else if (chunk.Type == Types.End && sprite_stack.Count > 0)
                    {
                        sprite_stack.Pop();
                    }
                    else if (IsSoundStream (chunk))
                    {
                        HandleSoundStream(chunk, ref current_stream, base_name, resource_groups);
                    }
                    else if (TypeMap.ContainsKey (chunk.Type))
                    {
                        var entry = CreateEntry (chunk, base_name);
                        if (entry != null)
                        {
                            if (sprite_stack.Count > 0)
                            {
                                sprite_stack.Peek().Children.Add(entry);
                            }
                            else
                            {
                                string group = GetResourceGroup (chunk.Type);
                                AddToResourceGroup (resource_groups, group, entry);
                            }
                        }
                    }
                }

                foreach (var group in resource_groups)
                {
                    if (group.Value.Count > 0)
                    {
                        var folder = new SwfEntry
                        {
                            Name = group.Key,
                            Type = "folder",
                            Offset = 0,
                            Size = 0,
                            Children = group.Value
                        };
                        root_entries.Add(folder);
                    }
                }

                var flat_list = FlattenHierarchy (root_entries);
                return new ArcFile (file, this, flat_list);
            }
        }

        private SwfEntry CreateEntry (SwfChunk chunk, string baseName)
        {
            var type = GetTypeFromId (chunk.Type);
            var name = GenerateResourceName (chunk, baseName);

            return new SwfEntry
            {
                Name = name,
                Type = type,
                Chunk = chunk,
                Offset = 0,
                Size = (uint)chunk.Length
            };
        }

        private string GenerateResourceName (SwfChunk chunk, string baseName)
        {
            string prefix = GetResourcePrefix (chunk.Type);
            string extension = GetResourceExtension (chunk.Type);

            if (chunk.Id >= 0)
                return string.Format ("{0}_{1:D5}.{2}", prefix, chunk.Id, extension);
            else
                return string.Format ("{0}_{1}.{2}", baseName, prefix, extension);
        }

        private string GetResourcePrefix (Types type)
        {
            switch (type)
            {
                case Types.DefineBitsJpeg:
                case Types.DefineBitsJpeg2:
                case Types.DefineBitsJpeg3:
                    return "Image";
                case Types.DefineBitsLossless:
                case Types.DefineBitsLossless2:
                    return "Image";
                case Types.DefineSound:
                    return "Sound";
                case Types.DefineShape:
                case Types.DefineShape2:
                case Types.DefineShape3:
                    return "Shape";
                case Types.DefineText:
                case Types.DefineText2:
                    return "Text";
                case Types.DefineFont:
                case Types.DefineFont2:
                case Types.DefineFont3:
                    return "Font";
                case Types.DefineButton:
                case Types.DefineButton2:
                    return "Button";
                case Types.DefineVideoStream:
                    return "Video";
                default:
                    return type.ToString();
            }
        }

        private string GetResourceExtension (Types type)
        {
            switch (type)
            {
                case Types.DefineBitsJpeg:
                case Types.DefineBitsJpeg2:
                case Types.DefineBitsJpeg3:
                    return "jpg";
                case Types.DefineBitsLossless:
                case Types.DefineBitsLossless2:
                    return "png";
                case Types.DefineSound:
                    return "mp3";
                case Types.DefineVideoStream:
                    return "flv";
                default:
                    return "dat";
            }
        }

        private string GetResourceGroup (Types type)
        {
            switch (type)
            {
                case Types.DefineBitsJpeg:
                case Types.DefineBitsJpeg2:
                case Types.DefineBitsJpeg3:
                case Types.DefineBitsLossless:
                case Types.DefineBitsLossless2:
                    return "Images";
                case Types.DefineSound:
                case Types.SoundStreamHead:
                case Types.SoundStreamHead2:
                    return "Audio";
                case Types.DefineShape:
                case Types.DefineShape2:
                case Types.DefineShape3:
                    return "Shapes";
                case Types.DefineText:
                case Types.DefineText2:
                    return "Text";
                case Types.DefineFont:
                case Types.DefineFont2:
                case Types.DefineFont3:
                    return "Fonts";
                case Types.DefineButton:
                case Types.DefineButton2:
                    return "Buttons";
                case Types.DefineVideoStream:
                case Types.VideoFrame:
                    return "Video";
                case Types.DoAction:
                case Types.DoInitAction:
                    return "Scripts";
                default:
                    return "Other";
            }
        }

        private void AddToResourceGroup (Dictionary<string, List<Entry>> groups, string groupName, Entry entry)
        {
            if (!groups.ContainsKey (groupName))
                groups[groupName] = new List<Entry>();
            groups[groupName].Add (entry);
        }

        private void HandleSoundStream (SwfChunk chunk, ref SwfSoundEntry currentStream, 
                                     string baseName, Dictionary<string, List<Entry>> resourceGroups)
        {
            switch (chunk.Type)
            {
                case Types.SoundStreamHead:
                case Types.SoundStreamHead2:
                    if ((chunk.Data[1] & 0x30) != 0x20) // not mp3 stream
                    {
                        currentStream = null;
                        return;
                    }
                    currentStream = new SwfSoundEntry
                    {
                        Name = string.Format ("SoundStream_{0:D5}.mp3", chunk.Id),
                        Type = "audio",
                        Chunk = chunk,
                        Offset = 0,
                    };
                    AddToResourceGroup (resourceGroups, "Audio", currentStream);
                    break;

                case Types.SoundStreamBlock:
                    if (currentStream != null)
                    {
                        currentStream.Size += (uint)(chunk.Data.Length - 4);
                        currentStream.SoundStream.Add(chunk);
                    }
                    break;
            }
        }

        private List<Entry> FlattenHierarchy (List<Entry> entries, string parentPath = "")
        {
            var result = new List<Entry>();

            foreach (var entry in entries)
            {
                var swfEntry = entry as SwfEntry;
                if (swfEntry != null)
                {
                    swfEntry.Path = string.IsNullOrEmpty(parentPath) 
                        ? entry.Name 
                        : parentPath + "/" + entry.Name;

                    if (swfEntry.Type != "folder")
                    {
                        swfEntry.Name = swfEntry.Path;
                        result.Add (swfEntry);
                    }

                    if (swfEntry.Children != null && swfEntry.Children.Count > 0)
                        result.AddRange (FlattenHierarchy(swfEntry.Children, swfEntry.Path));
                }
            }

            return result;
        }

        public override Stream OpenEntry (ArcFile arc, Entry entry)
        {
            var swent = (SwfEntry)entry;

            Extractor extract;
            if (!ExtractMap.TryGetValue (swent.Chunk.Type, out extract))
                extract = ExtractChunk;
            return extract (swent);
        }

        static string GetTypeFromId (Types type_id)
        {
            string type;
            if (TypeMap.TryGetValue (type_id, out type))
                return type;
            return type_id.ToString();
        }

        static Stream ExtractChunk (SwfEntry entry)
        {
            return new BinMemoryStream (entry.Chunk.Data);
        }

        static Stream ExtractChunkContents (SwfEntry entry)
        {
            var source = entry.Chunk;
            return new BinMemoryStream (source.Data, 2, source.Length-2);
        }

        static Stream ExtractSoundStream (SwfEntry entry)
        {
            var swe = (SwfSoundEntry)entry;
            var output = new MemoryStream ((int)swe.Size);
            foreach (var chunk in swe.SoundStream)
                output.Write (chunk.Data, 4, chunk.Data.Length-4);
            output.Position = 0;
            return output;
        }

        static Stream ExtractAudio (SwfEntry entry)
        {
            var chunk = entry.Chunk;
            int flags = chunk.Data[2];
            int format = flags >> 4;
            if (2 == format) // MP3
                return new BinMemoryStream (chunk.Data, 9, chunk.Length-9);

            // For other formats, include header info
            return new BinMemoryStream (chunk.Data, 2, chunk.Length-2);
        }

        public override IImageDecoder OpenImage (ArcFile arc, Entry entry)
        {
            var swent = (SwfEntry)entry;
            switch (swent.Chunk.Type)
            {
            case Types.DefineBitsLossless:
            case Types.DefineBitsLossless2:
                return new LosslessImageDecoder (swent.Chunk);
            case Types.DefineBitsJpeg2:
                return new SwfJpeg2Decoder (swent.Chunk);
            case Types.DefineBitsJpeg3:
                return new SwfJpeg3Decoder (swent.Chunk);
            case Types.DefineBitsJpeg:
                return OpenBitsJpeg (swent.Chunk);
            default:
                return base.OpenImage (arc, entry);
            }
        }

        IImageDecoder OpenBitsJpeg (SwfChunk chunk)
        {
            int jpeg_pos = 0;
            for (int i = 0; i < chunk.Data.Length - 2; ++i)
            {
                if (chunk.Data[i] == 0xFF && chunk.Data[i+1] == 0xD8)
                {
                    jpeg_pos = i;
                    break;
                }
            }
            var input = new BinMemoryStream (chunk.Data, jpeg_pos, chunk.Data.Length - jpeg_pos);
            return ImageFormatDecoder.Create (input);
        }

        static Stream ExtractJpeg2(SwfEntry entry)
        {
            var chunk = entry.Chunk;
            int jpeg_pos = 2; // Start after ID

            for (int i = 2; i < chunk.Data.Length - 1; i++)
            {
                // Find JPEG SOI marker
                if (chunk.Data[i] == 0xFF && chunk.Data[i + 1] == 0xD8)
                {
                    jpeg_pos = i + 2;
                    break;
                }
            }

            return new BinMemoryStream(chunk.Data, jpeg_pos, chunk.Data.Length - jpeg_pos);
        }

        static Stream ExtractJpeg3(SwfEntry entry)
        {
            var chunk = entry.Chunk;
            int jpeg_length = chunk.Data.ToInt32(2);
            return new BinMemoryStream(chunk.Data, 6, jpeg_length);
        }

        delegate Stream Extractor (SwfEntry entry);

        static Dictionary<Types, Extractor> ExtractMap = new Dictionary<Types, Extractor> {
            { Types.DefineBitsJpeg,      ExtractChunkContents },
            { Types.DefineBitsJpeg2,     ExtractJpeg2 },
            { Types.DefineBitsJpeg3,     ExtractJpeg3 },
            { Types.DefineBitsLossless,  ExtractChunk },
            { Types.DefineBitsLossless2, ExtractChunk },
            { Types.DefineSound,         ExtractAudio },
            { Types.SoundStreamHead,     ExtractSoundStream },
            { Types.SoundStreamHead2,    ExtractSoundStream },
            { Types.DoAction,            ExtractChunkContents },
        };

        static Dictionary<Types, string> TypeMap = new Dictionary<Types, string> {
            { Types.DefineBitsJpeg,         "image" },
            { Types.DefineBitsJpeg2,        "image" },
            { Types.DefineBitsJpeg3,        "image" },
            { Types.DefineBitsLossless,     "image" },
            { Types.DefineBitsLossless2,    "image" },
            { Types.DefineSound,            "audio" },
            { Types.DoAction,               "script" },
            { Types.DoInitAction,           "script" },
            { Types.DefineShape,            "shape" },
            { Types.DefineShape2,           "shape" },
            { Types.DefineShape3,           "shape" },
            { Types.DefineText,             "text" },
            { Types.DefineText2,            "text" },
            { Types.DefineFont,             "font" },
            { Types.DefineFont2,            "font" },
            { Types.DefineFont3,            "font" },
            { Types.DefineButton,           "button" },
            { Types.DefineButton2,          "button" },
            { Types.DefineSprite,           "sprite" },
            { Types.DefineVideoStream,      "video" },
            { Types.VideoFrame,             "video" },
            { Types.JpegTables,             "data" },
            { Types.DefineMorphShape,       "shape" },
            { Types.DefineMorphShape2,      "shape" },
            { Types.DefineBinary,           "binary" },
            { Types.DefineEditText,         "text" },
            { Types.PlaceObject,            "placement" },
            { Types.PlaceObject2,           "placement" },
            { Types.PlaceObject3,           "placement" },
            { Types.RemoveObject,           "placement" },
            { Types.RemoveObject2,          "placement" },
        };

        internal static bool IsSoundStream (SwfChunk chunk)
        {
            return chunk.Type == Types.SoundStreamHead
                || chunk.Type == Types.SoundStreamHead2
                || chunk.Type == Types.SoundStreamBlock;
        }
    }

    internal enum Types : short
    {
        End                     = 0,
        ShowFrame               = 1,
        DefineShape             = 2,
        PlaceObject             = 4,
        RemoveObject            = 5,
        DefineBitsJpeg          = 6,
        DefineButton            = 7,
        JpegTables              = 8,
        SetBackgroundColor      = 9,
        DefineFont              = 10,
        DefineText              = 11,
        DoAction                = 12,
        DefineFontInfo          = 13,
        DefineSound             = 14,
        StartSound              = 15,
        DefineButtonSound       = 17,
        SoundStreamHead         = 18,
        SoundStreamBlock        = 19,
        DefineBitsLossless      = 20,
        DefineBitsJpeg2         = 21,
        DefineShape2            = 22,
        DefineButtonCxform      = 23,
        Protect                 = 24,
        PlaceObject2            = 26,
        RemoveObject2           = 28,
        DefineShape3            = 32,
        DefineText2             = 33,
        DefineButton2           = 34,
        DefineBitsJpeg3         = 35,
        DefineBitsLossless2     = 36,
        DefineEditText          = 37,
        DefineSprite            = 39,
        FrameLabel              = 43,
        SoundStreamHead2        = 45,
        DefineMorphShape        = 46,
        DefineFont2             = 48,
        ExportAssets            = 56,
        ImportAssets            = 57,
        EnableDebugger          = 58,
        DoInitAction            = 59,
        DefineVideoStream       = 60,
        VideoFrame              = 61,
        DefineFontInfo2         = 62,
        EnableDebugger2         = 64,
        ScriptLimits            = 65,
        SetTabIndex             = 66,
        FileAttributes          = 69,
        PlaceObject3            = 70,
        ImportAssets2           = 71,
        DefineFontAlignZones    = 73,
        CSMTextSettings         = 74,
        DefineFont3             = 75,
        SymbolClass             = 76,
        Metadata                = 77,
        DefineScalingGrid       = 78,
        DoABC                   = 82,
        DefineShape4            = 83,
        DefineMorphShape2       = 84,
        DefineSceneAndFrameLabelData = 86,
        DefineBinary            = 87,
        DefineFontName          = 88,
        StartSound2             = 89,
        DefineBitsJpeg4         = 90,
        DefineFont4             = 91,
    };

    internal class SwfChunk
    {
        public Types    Type;
        public byte[]   Data;

        public int Length { get { return Data.Length; } }
        public int     Id { get { return Data.Length > 2 ? Data.ToUInt16 (0) : -1; } }

        public SwfChunk (Types id, int length)
        {
            Type = id;
            Data = length > 0 ? new byte[length] : Array.Empty<byte>();
        }
    }

    internal sealed class SwfReader : IDisposable
    {
        IBinaryStream   m_input;
        MsbBitStream    m_bits;
        int             m_version;

        Int32Rect       m_dim;

        public SwfReader (IBinaryStream input, int version, bool is_compressed)
        {
            m_input = input;
            m_version = version;
            m_input.Position = 8;
            if (is_compressed)
            {
                var zstream = new ZLibStream (input.AsStream, CompressionMode.Decompress);
                m_input = new BinaryStream (zstream, m_input.Name);
            }
            m_bits = new MsbBitStream (m_input.AsStream, true);
        }

        int     m_frame_rate;
        int     m_frame_count;

        List<SwfChunk>  m_chunks = new List<SwfChunk>();

        public List<SwfChunk> Parse ()
        {
            ReadDimensions();
            m_bits.Reset();
            m_frame_rate = m_input.ReadUInt16();
            m_frame_count = m_input.ReadUInt16();

            // Read all chunks
            for (;;)
            {
                var chunk = ReadChunk();
                if (null == chunk)
                    break;
                m_chunks.Add (chunk);

                if (chunk.Type == Types.DefineSprite)
                    ReadSpriteContents(chunk);
            }
            return m_chunks;
        }

        void ReadSpriteContents(SwfChunk spriteChunk)
        {
            using (var spriteData = new BinMemoryStream(spriteChunk.Data))
            {
                spriteData.Position = 4;

                using (var spriteBits = new MsbBitStream(spriteData, true))
                {

                    var originalInput = m_input;
                    var originalBits = m_bits;

                    m_input = new BinaryStream(spriteData, m_input.Name);
                    m_bits = spriteBits;

                    try
                    {
                        for (;;)
                        {
                            var chunk = ReadChunk();
                            if (null == chunk)
                                break;
                            m_chunks.Add(chunk);
                            if (chunk.Type == Types.End)
                                break;
                        }
                    }
                    finally
                    {
                        m_input = originalInput;
                        m_bits = originalBits;
                    }
                }
            }
        }

        void ReadDimensions ()
        {
            int rsize = m_bits.GetBits (5);
            m_dim.X = GetSignedBits (rsize);
            m_dim.Width = GetSignedBits (rsize) - m_dim.X;
            m_dim.Y = GetSignedBits (rsize);
            m_dim.Height = GetSignedBits (rsize) - m_dim.Y;
        }

        byte[]  m_buffer = new byte[4];

        SwfChunk ReadChunk ()
        {
            if (m_input.Read (m_buffer, 0, 2) != 2)
                return null;
            int length = m_buffer.ToUInt16 (0);
            Types id = (Types)(length >> 6);
            length &= 0x3F;
            if (0x3F == length)
                length = m_input.ReadInt32();

            var chunk = new SwfChunk (id, length);
            if (length > 0)
            {
                if (m_input.Read (chunk.Data, 0, length) < length)
                    return null;
            }
            return chunk;
        }

        int GetSignedBits (int count)
        {
            int v = m_bits.GetBits (count);
            if ((v >> (count - 1)) != 0)
                v |= -1 << count;
            return v;
        }

        #region IDisposable Members
        bool m_disposed = false;
        public void Dispose ()
        {
            if (!m_disposed)
            {
                m_input.Dispose();
                m_bits.Dispose();
                m_disposed = true;
            }
        }
        #endregion
    }

    internal sealed class LosslessImageDecoder : BinaryImageDecoder
    {
        Types           m_type;
        int             m_colors;
        int             m_data_pos;

        public PixelFormat       Format { get; private set; }
        private bool           HasAlpha { get { return m_type == Types.DefineBitsLossless2; } }

        public LosslessImageDecoder (SwfChunk chunk) : base (new BinMemoryStream (chunk.Data))
        {
            m_type = chunk.Type;
            byte format = chunk.Data[2];
            int bpp;
            switch (format)
            {
            case 3:
                bpp = 8; Format = PixelFormats.Indexed8;
                break;
            case 4:
                bpp = 16; Format = PixelFormats.Bgr565;
                break;
            case 5:
                bpp = 32;
                Format = HasAlpha ? PixelFormats.Bgra32 : PixelFormats.Bgr32;
                break;
            default: 
                throw new InvalidFormatException();
            }
            uint width  = chunk.Data.ToUInt16 (3);
            uint height = chunk.Data.ToUInt16 (5);
            m_colors = 0;
            m_data_pos = 7;
            if (3 == format)
                m_colors = chunk.Data[m_data_pos++] + 1;
            Info = new ImageMetaData {
                Width = width, Height = height, BPP = bpp
            };
        }

        protected override ImageData GetImageData ()
        {
            m_input.Position = m_data_pos;
            using (var input = new ZLibStream (m_input.AsStream, CompressionMode.Decompress, true))
            {
                BitmapPalette palette = null;
                if (8 == Info.BPP)
                {
                    var pal_format = HasAlpha ? PaletteFormat.RgbA : PaletteFormat.RgbX;
                    palette = ImageFormat.ReadPalette (input, m_colors, pal_format);
                }

                int stride = (int)Info.Width * (Info.BPP / 8);
                var pixels = new byte[(int)Info.Height * stride];

                if (8 == Info.BPP)
                {
                    // Read indexed data with proper row alignment
                    int row_size = (int)Info.Width;
                    int aligned_row_size = (row_size + 3) & ~3; // 4-byte alignment
                    var row_buffer = new byte[aligned_row_size];

                    for (int y = 0; y < Info.Height; y++)
                    {
                        input.Read(row_buffer, 0, aligned_row_size);
                        Buffer.BlockCopy(row_buffer, 0, pixels, y * row_size, row_size);
                    }
                }
                else
                {
                    input.Read (pixels, 0, pixels.Length);
                }

                if (32 == Info.BPP)
                {
                    // Convert ARGB to BGRA
                    for (int i = 0; i < pixels.Length; i += 4)
                    {
                        byte a = pixels[i];
                        byte r = pixels[i+1];
                        byte g = pixels[i+2];
                        byte b = pixels[i+3];
                        pixels[i]   = b;
                        pixels[i+1] = g;
                        pixels[i+2] = r;
                        pixels[i+3] = a;
                    }
                }
                return ImageData.Create (Info, Format, palette, pixels);
            }
        }
    }

    internal class JpegWithSignture
    {

        internal byte[]            m_input;
        internal ImageData         m_image;

        public Stream            Source { get { return Stream.Null; } }
        public ImageFormat SourceFormat { get { return null; } }
        public ImageData          Image { get { return m_image ?? (m_image = Unpack()); } }
        public ImageMetaData       Info { get; set; }


        public int FindJpegSignature(int start = 2)
        {
            int jpeg_pos = start;
            while (jpeg_pos < m_input.Length - 4)
            {
                if (m_input[jpeg_pos] != 0xFF)
                    jpeg_pos++;
                else if (m_input[jpeg_pos + 1] == 0xD8)
                    return jpeg_pos;
                else if (m_input[jpeg_pos + 1] != 0xD9)
                    jpeg_pos++;
                else if (m_input[jpeg_pos + 2] != 0xFF)
                    jpeg_pos += 3;
                else if (m_input[jpeg_pos + 3] != 0xD8)
                    jpeg_pos += 2;
                else
                    return jpeg_pos + 4;
            }
            return -1;
        }

        virtual public ImageData Unpack() { return null; }
        public void Dispose() { }
    }

    internal sealed class SwfJpeg2Decoder : JpegWithSignture, IImageDecoder
    {

        public SwfJpeg2Decoder (SwfChunk chunk)
        {
            m_input = chunk.Data;
        }

        override public ImageData Unpack()
        {
            int jpeg_pos = FindJpegSignature();
            if (jpeg_pos < 0)
                throw new InvalidFormatException("JPEG signature not found");

            using (var jpeg = new BinMemoryStream(m_input, jpeg_pos, m_input.Length - jpeg_pos))
            {
                try
                {
                    var decoder = new JpegBitmapDecoder(jpeg, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    var frame = decoder.Frames[0];
                    Info = new ImageMetaData
                    {
                        Width = (uint)frame.PixelWidth,
                        Height = (uint)frame.PixelHeight,
                        BPP = frame.Format.BitsPerPixel,
                    };
                    return new ImageData(frame, Info);
                }
                catch (Exception ex)
                {
                    throw new InvalidFormatException($"JPEG decode failed: {ex.Message}");
                }
            }
        }
    }

    internal sealed class SwfJpeg3Decoder : JpegWithSignture, IImageDecoder
    {
        int m_jpeg_length;

        public PixelFormat       Format { get; private set; }

        public SwfJpeg3Decoder (SwfChunk chunk)
        {
            m_input = chunk.Data;
        }

        override public ImageData Unpack ()
        {
            BitmapSource image;
            int base_offset = 6;
            m_jpeg_length = m_input.ToInt32 (2);
            try
            {
                using (var jpeg = new BinMemoryStream(m_input, base_offset, m_jpeg_length))
                {
                    var decoder = new JpegBitmapDecoder(jpeg, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    image = decoder.Frames[0];
                }
            }
            catch (FileFormatException)
            {
                base_offset = FindJpegSignature(20);
                if (base_offset < 0)
                    throw new InvalidFormatException("JPEG signature not found");

                using (var jpeg = new BinMemoryStream(m_input, base_offset, m_input.Length - base_offset))
                {
                    var decoder = new JpegBitmapDecoder(jpeg, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    image = decoder.Frames[0];
                }
            }

            Info = new ImageMetaData {
                Width = (uint)image.PixelWidth,
                Height = (uint)image.PixelHeight,
                BPP = 32,
            };

            int stride = image.PixelWidth * 4;
            var pixels = new byte[stride * image.PixelHeight];
            byte[] alpha = new byte[image.PixelWidth * image.PixelHeight];
            bool useAlpha = false;

            try
            {
                using (var input = new BinMemoryStream(m_input, base_offset + m_jpeg_length, m_input.Length - (base_offset + m_jpeg_length)))
                using (var alpha_data = new ZLibStream(input, CompressionMode.Decompress))
                    alpha_data.Read(alpha, 0, alpha.Length);

                if (image.Format.BitsPerPixel != 32)
                    image = new FormatConvertedBitmap(image, PixelFormats.Bgr32, null, 0);
                useAlpha = true;
            } catch
            {
                useAlpha = false;
            }

            image.CopyPixels(pixels, stride, 0);

            if (useAlpha)
              ApplyAlpha(pixels, alpha);

            return ImageData.Create (Info, PixelFormats.Bgra32, null, pixels, stride);
        }

        void ApplyAlpha (byte[] pixels, byte[] alpha)
        {
            int src = 0;
            for (int dst = 3; dst < pixels.Length; dst += 4)
                pixels[dst] = alpha[src++];
        }
    }
}
