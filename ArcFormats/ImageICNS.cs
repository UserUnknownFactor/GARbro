//! \file       ImageICNS.cs
//! \date       2025 Jul 10
//! \brief      ICNS format.
//
// Copyright (C) 2025 by morkt and others
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
using System.ComponentModel.Composition;
using System.IO;
using System.Collections.Generic;
using GameRes.Utility;

namespace GameRes.Formats.Apple
{
    [Export(typeof(ImageFormat))]
    public class IcnsFormat : ImageFormat
    {
        public override string         Tag { get { return "ICNS"; } }
        public override string Description { get { return "macOS icon format"; } }
        public override uint     Signature { get { return 0x736E6369; } } // 'icns'
        public override bool      CanWrite { get { return false; } }

        static readonly Dictionary<uint, IconType> IconTypes = new Dictionary<uint, IconType>
        {
            { 0x69633038, new IconType { Size = 16, Format = "PNG" } },   // 'ic08' - 16x16
            { 0x69633039, new IconType { Size = 32, Format = "PNG" } },   // 'ic09' - 32x32
            { 0x69633130, new IconType { Size = 64, Format = "PNG" } },   // 'ic10' - 64x64
            { 0x69633131, new IconType { Size = 128, Format = "PNG" } },  // 'ic11' - 128x128
            { 0x69633132, new IconType { Size = 256, Format = "PNG" } },  // 'ic12' - 256x256
            { 0x69633133, new IconType { Size = 512, Format = "PNG" } },  // 'ic13' - 512x512
            { 0x69633134, new IconType { Size = 1024, Format = "PNG" } }, // 'ic14' - 1024x1024
        };

        class IconType
        {
            public int Size { get; set; }
            public string Format { get; set; }
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (8);
            if (header.ToUInt32 (0) != Signature)
                return null;

            uint file_size = Binary.BigEndian (header.ToUInt32 (4));
            if (file_size != file.Length)
                return null;

            // Find largest icon
            file.Seek(8, SeekOrigin.Begin);
            int max_size = 0;
            
            while (file.Position < file.Length - 8)
            {
                uint type = Binary.BigEndian (file.ReadUInt32());
                uint size = Binary.BigEndian (file.ReadUInt32());
                
                if (IconTypes.TryGetValue (type, out var icon_type))
                {
                    if (icon_type.Size > max_size)
                        max_size = icon_type.Size;
                }
                
                file.Seek (size - 8, SeekOrigin.Current);
            }

            if (max_size == 0)
                return null;

            return new ImageMetaData
            {
                Width = (uint)max_size,
                Height = (uint)max_size,
                BPP = 32
            };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            file.Seek(8, SeekOrigin.Begin);

            // Find the icon matching the metadata size
            while (file.Position < file.Length - 8)
            {
                uint type = Binary.BigEndian (file.ReadUInt32());
                uint size = Binary.BigEndian (file.ReadUInt32());

                if (IconTypes.TryGetValue(type, out var icon_type))
                {
                    if (icon_type.Size == info.Width)
                    {
                        if (icon_type.Format == "PNG")
                        {
                            var data = file.ReadBytes((int)(size - 8));
                            using (var png = new BinMemoryStream(data))
                                return Png.Read(png, info);
                        }
                    }
                }
                file.Seek (size - 8, SeekOrigin.Current);
            }

            throw new InvalidFormatException ("No PNG icons found in ICNS file");
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("IcnsFormat.Write not implemented");
        }
    }
}