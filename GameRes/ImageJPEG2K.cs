using System;
using System.IO;
using System.Text;
using System.ComponentModel.Composition;
using System.Windows.Media.Imaging;
using GameRes.Strings;
using GameRes.Utility;

namespace GameRes
{
    [Export(typeof(ImageFormat))]
    [ExportMetadata("Priority", 10)]
    public class Jpeg2000Format : ImageFormat
    {
        public override string         Tag { get { return "JP2"; } }
        public override string Description { get { return "JPEG 2000 image file format"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        readonly FixedGaugeSetting Quality = new FixedGaugeSetting (Properties.Settings.Default) {
            Name = "JP2Quality",
            Text = "JPEG 2000 compression quality",
            Min = 1, Max = 100,
            ValuesSet = new[] { 1, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 },
        };

        public Jpeg2000Format ()
        {
            Extensions = new string[] { "jp2", "j2k", "jpf", "jpx", "jpm", "j2c" };
            Signatures = new uint[] {
                0x0C000000,  // JP2: 00 00 00 0C (little-endian)
                0x51FF4FFF,  // J2K: FF 4F FF 51 (little-endian)
            };
            Settings = new[] { Quality };
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            throw new NotImplementedException("Jpeg2000Format.Read is not implemented");
            /*
            var decoder = BitmapDecoder.Create (file.AsStream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            frame.Freeze();
            return new ImageData (frame, info);
            */
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException("Jpeg2000Format.Write is not implemented");
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            switch(file.Signature) {
            case 0x0C000000:
                file.Position = 0;
                return ReadJP2MetaData (file);
            case 0x51FF4FFF:
                file.Position = 0;
                return ReadJ2KMetaData (file);
            default:
                return null;
            }
        }

        private ImageMetaData ReadJP2MetaData (IBinaryStream file)
        {
            while (file.Position < file.Length)
            {
                if (file.Length - file.Position < 8)
                    return null;
                    
                uint box_length = Binary.BigEndian (file.ReadUInt32());
                uint box_type = file.ReadUInt32();
                
                if (box_type == 0x69686472) // 'ihdr'
                {
                    if (box_length < 22)
                        return null;
                        
                    uint height = Binary.BigEndian (file.ReadUInt32());
                    uint width = Binary.BigEndian (file.ReadUInt32());
                    ushort components = Binary.BigEndian (file.ReadUInt16());
                    byte bpc = file.ReadUInt8(); // bits per component
                    
                    return new ImageMetaData {
                        Width = width,
                        Height = height,
                        BPP = bpc * components,
                    };
                }
                
                // Skip to next box
                if (box_length == 0)
                    break;
                else if (box_length == 1)
                {
                    file.Seek (8, SeekOrigin.Current); // Skip 64-bit length
                    continue;
                }
                else
                    file.Seek (box_length - 8, SeekOrigin.Current);
            }
            
            return null;
        }

        private ImageMetaData ReadJ2KMetaData (IBinaryStream file)
        {
            ushort marker = Binary.BigEndian (file.ReadUInt16());
            if (marker != 0xFF4F) // SOC marker
                return null;
                
            while (file.Position < file.Length)
            {
                marker = Binary.BigEndian (file.ReadUInt16());
                if (marker == 0xFF51) // SIZ marker
                {
                    ushort length = Binary.BigEndian (file.ReadUInt16());
                    if (length < 41)
                        return null;
                        
                    ushort caps = Binary.BigEndian (file.ReadUInt16());
                    uint width = Binary.BigEndian (file.ReadUInt32());
                    uint height = Binary.BigEndian (file.ReadUInt32());
                    uint x_offset = Binary.BigEndian (file.ReadUInt32());
                    uint y_offset = Binary.BigEndian (file.ReadUInt32());
                    uint tile_width = Binary.BigEndian (file.ReadUInt32());
                    uint tile_height = Binary.BigEndian (file.ReadUInt32());
                    uint tile_x_offset = Binary.BigEndian (file.ReadUInt32());
                    uint tile_y_offset = Binary.BigEndian (file.ReadUInt32());
                    ushort components = Binary.BigEndian (file.ReadUInt16());
                    
                    // Read component info
                    byte precision = file.ReadUInt8();
                    byte bpp = (byte)((precision & 0x7F) + 1);
                    
                    return new ImageMetaData {
                        Width = width - x_offset,
                        Height = height - y_offset,
                        BPP = bpp * components,
                    };
                }
                
                // Skip other markers
                if ((marker & 0xFF00) == 0xFF00 && marker != 0xFFFF)
                {
                    ushort seg_length = Binary.BigEndian (file.ReadUInt16());
                    file.Seek (seg_length - 2, SeekOrigin.Current);
                }
                else
                    break;
            }
            
            return null;
        }
    }
}