using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameRes.Utility;

// [030502][Studio B-Room] Wataridori ni Yadorigi o
// [040604][Studio B-Room] Dice-ki! ~Koi wa Un Makase~

namespace GameRes.Formats.BRoom
{
    internal class ErpMetaData : ImageMetaData
    {
        public int  Id;
        public int  Method;
        public byte KeyIndex;
    }

    [Export(typeof(ImageFormat))]
    public class ErpFormat : ImageFormat
    {
        public override string         Tag { get { return "ERP"; } }
        public override string Description { get { return "Studio B-Room image format"; } }
        public override uint     Signature { get { return 0; } }

        static readonly byte[] HeaderKey = { 
            0x7D, 0x45, 0x59, 0x26, 0x8D, 0x45, 0x98, 0x26, 0x69, 0x68, 0x57, 0x52,
            0x76, 0x85, 0x12, 0x18, 0x62, 0x47, 0x7F, 0x84,
        };

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            if ((file.Signature & 0xFFFFFF00) != 0x26594500)
                return null;
            var header = file.ReadHeader (0x14).ToArray();
            int idx = header[0] ^ HeaderKey[0];
            if (idx >= 31)
                return null;
            for (int i = 4; i < 0x14; ++i)
                header[i] ^= HeaderKey[i];
            int method = header.ToInt32 (16) ^ idx;
            if (method < 0 || method > 12)
                return null;
            int key_index = GuessKeyIndex (header, idx);
            if (key_index < 0)
                return null;
            int bpp = header.ToInt32 (4) ^ DefaultKey[0, key_index, idx];
            if (bpp != 8 && bpp != 24)
                return null;
            return new ErpMetaData {
                Width  = (header.ToUInt32 (8)  ^ DefaultKey[1, key_index, idx]) * 4,
                Height = (header.ToUInt32 (12) ^ DefaultKey[2, key_index, idx]) * 4,
                BPP = bpp,
                Id = idx,
                Method = method,
                KeyIndex = (byte)key_index,
            };
        }

        int? LastKeyIndex = null;

        int GuessKeyIndex (byte[] header, int idx)
        {
            if (LastKeyIndex != null)
                return LastKeyIndex.Value;
            for (int key_index = 2; key_index >= 0; --key_index)
            {
                int bpp = header[4] ^ DefaultKey[0, key_index, idx];
                if (bpp == 8 || bpp == 24)
                {
                    if (idx != 9)
                        LastKeyIndex = key_index;
                    return key_index;
                }
            }
            LastKeyIndex = null;
            return -1;
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var reader = new ErpReader (file, (ErpMetaData)info);
            return reader.Unpack();
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("ErpFormat.Write not implemented");
        }

        static readonly byte[,,] DefaultKey = new byte[3,3,31] {
            {
                { 0x86, 0xBD, 0xAD, 0x4B, 0xBC, 0x1D, 0xAA, 0x4C, 0x23, 0xAB, 0x16, 0x8C, 0xE2, 0x29, 0x7F, 0xE1,
                  0xF3, 0xAC, 0x9A, 0x09, 0x30, 0xD4, 0x7D, 0x7B, 0x32, 0x70, 0x73, 0x81, 0x5B, 0x3C, 0x47 },
                { 0x2E, 0x90, 0x20, 0x6C, 0x59, 0xB2, 0x83, 0x71, 0x12, 0x13, 0x87, 0xC4, 0xF4, 0x3E, 0xD9, 0x93,
                  0x0B, 0xDC, 0xB3, 0x70, 0xCC, 0x53, 0xDA, 0x96, 0xA6, 0xA2, 0xB8, 0x43, 0x07, 0xD3, 0x68 },
                { 0x36, 0x89, 0x53, 0xE9, 0x7E, 0x07, 0x3A, 0x80, 0x18, 0x13, 0xD5, 0xD2, 0x70, 0x2B, 0xCB, 0xC7,
                  0xA3, 0xF5, 0xE4, 0x8E, 0x29, 0x48, 0xDC, 0xAE, 0x0E, 0xBB, 0xC9, 0xA8, 0x3C, 0xA7, 0x50 }
            }, {
                { 0x33, 0x2A, 0x40, 0xB4, 0x50, 0xDA, 0x96, 0xE7, 0x8B, 0x54, 0x06, 0x85, 0x1B, 0xD7, 0x79, 0x0A,
                  0x83, 0xB9, 0x9C, 0x0D, 0x63, 0x2E, 0x5D, 0x6B, 0xF2, 0x39, 0xEE, 0x55, 0xCE, 0x4D, 0xA0 },
                { 0x54, 0xD5, 0x18, 0x74, 0x72, 0x79, 0x37, 0x98, 0xBA, 0x4F, 0x49, 0x9D, 0xB7, 0x40, 0xE7, 0x76,
                  0xC1, 0x1C, 0x91, 0x3B, 0x2C, 0x1D, 0x2B, 0x5D, 0x6F, 0x1E, 0x8E, 0x82, 0xC6, 0xBC, 0x33 },
                { 0x28, 0xB1, 0x85, 0xF4, 0xFA, 0x9C, 0xEF, 0x73, 0x34, 0x00, 0x22, 0x76, 0x78, 0x45, 0x4A, 0xD8,
                  0x5D, 0x0F, 0x3F, 0xB9, 0xA9, 0xA0, 0x46, 0xCD, 0x93, 0x91, 0x66, 0x2C, 0xE3, 0x26, 0x10 }
            }, {
                { 0xA7, 0x35, 0xBB, 0x42, 0x82, 0x60, 0x71, 0x26, 0xB1, 0x69, 0xB6, 0x2D, 0x0F, 0x6D, 0xA3, 0xB0,
                  0xCA, 0x10, 0x4F, 0xDC, 0x00, 0xA2, 0xA6, 0x04, 0x9B, 0x7E, 0xC6, 0xFF, 0xFE, 0xFA, 0x7C },
                { 0x95, 0x34, 0x0D, 0xA4, 0x3C, 0x30, 0x6E, 0xBB, 0xD1, 0x35, 0xF5, 0x3A, 0xA9, 0xBF, 0x8A, 0x3D,
                  0x31, 0xFA, 0x19, 0x45, 0x6D, 0x7E, 0x24, 0x9B, 0xAF, 0x1B, 0x65, 0xB9, 0x02, 0xFF, 0x0C },
                { 0xD0, 0x74, 0x77, 0x1E, 0x54, 0xBE, 0xD1, 0x44, 0xE0, 0x9D, 0xFE, 0x40, 0xC8, 0x31, 0x0B, 0xB2,
                  0x5E, 0x92, 0xF1, 0x0A, 0x84, 0x64, 0x35, 0xF2, 0x9B, 0x19, 0x9E, 0x83, 0x0C, 0x14, 0x7A }
            }
        };
    }

    internal class ErpReader
    {
        IBinaryStream   m_input;
        ErpMetaData     m_info;
        byte[]          m_output;

        public PixelFormat    Format { get; private set; }
        public BitmapPalette Palette { get; private set; }

        public ErpReader (IBinaryStream input, ErpMetaData info)
        {
            m_input = input;
            m_info = info;
            int stride = info.iWidth * info.BPP / 8;
            m_output = new byte[stride * info.iHeight];
            if (8 == m_info.BPP)
                Format = PixelFormats.Indexed8;
            else if (24 == m_info.BPP)
                Format = PixelFormats.Bgr24;
            else
                throw new InvalidFormatException();
        }

        public ImageData Unpack ()
        {
            m_input.Position = 0x14;
            if (8 == m_info.BPP)
            {
                Palette = ImageFormat.ReadPalette (m_input.AsStream);
                Unpack8bpp();
            }
            else if (0 == m_info.Method)
                UnpackV0();
            else if (m_info.Method < 7)
                UnpackV1();
            else
                UnpackV7();
            return ImageData.Create (m_info, Format, Palette, m_output);
        }

        void Unpack8bpp ()
        {
            int dst = 0;
            while (dst < m_output.Length)
            {
                int px = m_input.ReadByte();
                int count = m_input.ReadByte();
                if (count < 0)
                    break;
                count ^= 9;
                if (count > 0)
                {
                    byte v = (byte)(px ^ 13);
                    while (count --> 0)
                        m_output[dst++] = v;
                }
            }
        }

        void UnpackV0 ()
        {
            var count_key = new ErpKey (m_info.Id ^ 0x68, DefaultKey[6, m_info.KeyIndex, m_info.Id]);
            var r_key = new ErpKey (DefaultKey[0, m_info.KeyIndex, m_info.Id],
                                    DefaultKey[3, m_info.KeyIndex, m_info.Id]);
            var g_key = new ErpKey (DefaultKey[1, m_info.KeyIndex, m_info.Id],
                                    DefaultKey[4, m_info.KeyIndex, m_info.Id]);
            var b_key = new ErpKey (DefaultKey[2, m_info.KeyIndex, m_info.Id],
                                    DefaultKey[5, m_info.KeyIndex, m_info.Id]);
            var rgb = new byte[3];
            int dst = 0;
            while (dst < m_output.Length)
            {
                m_input.Read (rgb, 0, 3);
                int count = m_input.ReadByte();
                if (count < 0)
                    break;
                count ^= count_key.Value ^ 0xE9;
                if (count > 0)
                {
                    m_output[dst++] = (byte)(rgb[0] ^ b_key.Value);
                    m_output[dst++] = (byte)(rgb[1] ^ g_key.Value);
                    m_output[dst++] = (byte)(rgb[2] ^ r_key.Value);
                    count = Math.Min ((count - 1) * 3, m_output.Length - dst);
                    if (count > 0)
                    {
                        Binary.CopyOverlapped (m_output, dst-3, dst, count);
                        dst += count;
                    }
                }
                r_key.Increment();
                g_key.Increment();
                b_key.Increment();
                count_key.Increment();
            }
        }

        static readonly byte[,] ChannelOrder = new byte[6,3] {
            { 0, 1, 2 }, { 0, 2, 1 }, { 1, 0, 2 }, { 1, 2, 0 }, { 2, 0, 1 }, { 2, 1, 0 }
        };

        void UnpackV1 ()
        {
            var count_list = new List<int>();
            var count_key = new ErpKey (m_info.Id ^ 0x68, DefaultKey[6, m_info.KeyIndex, m_info.Id]);
            int count;
            while ((count = m_input.ReadByte()) >= 0)
            {
                count ^= count_key.Value ^ 0xE9;
                if (0 == count)
                    break;
                count_list.Add (count);
                count_key.Increment();
            }
            int order = m_info.Method - 1;
            for (int i = 0; i < 3; ++i)
            {
                int channel = ChannelOrder[order, i];
                var pixel_key = new ErpKey (DefaultKey[channel, m_info.KeyIndex, m_info.Id],
                                            DefaultKey[channel+3, m_info.KeyIndex, m_info.Id]);
                int dst = 2-channel;
                for (int j = 0; j < count_list.Count; ++j)
                {
                    count = count_list[j];
                    int px = m_input.ReadByte();
                    byte v = (byte)(px ^ pixel_key.Value);
                    while (count --> 0)
                    {
                        m_output[dst] = v;
                        dst += 3;
                    }
                    pixel_key.Increment();
                }
            }
        }

        void UnpackV7 ()
        {
            int order = m_info.Method - 7;
            var channels = new byte[3][];
            for (int i = 0; i < 3; ++i)
            {
                int channel = ChannelOrder[order, i];
                channels[channel] = UnpackChannel (channel);
            }
            int src = 0;
            int dst = 0;
            while (dst < m_output.Length)
            {
                m_output[dst++] = channels[2][src];
                m_output[dst++] = channels[1][src];
                m_output[dst++] = channels[0][src];
                ++src;
            }
        }

        byte[] UnpackChannel (int channel)
        {
            var output = new byte[m_output.Length / 3];
            var count_key = new ErpKey (m_info.Id ^ 0x68, DefaultKey[6, m_info.KeyIndex, m_info.Id]);
            var pixel_key = new ErpKey (DefaultKey[channel, m_info.KeyIndex, m_info.Id],
                                        DefaultKey[channel+3, m_info.KeyIndex, m_info.Id]);
            int dst = 0;
            for (;;)
            {
                int px = m_input.ReadByte();
                int count = m_input.ReadByte();
                if (count < 0)
                    break;
                count ^= count_key.Value ^ 0xE9;
                if (0 == count)
                    break;
                count = Math.Min (count, output.Length - dst);
                byte v = (byte)(px ^ pixel_key.Value);
                while (count --> 0)
                    output[dst++] = v;

                count_key.Increment();
                pixel_key.Increment();
            }
            return output;
        }

        static readonly byte[,,] DefaultKey = new byte[7,3,31] {
            {
                { 0x5C, 0xEC, 0xC5, 0xCB, 0x76, 0xEF, 0x08, 0x66, 0xBE, 0x0E, 0x05, 0x9E, 0x1F, 0x8D, 0x11, 0xCD,
                  0xE5, 0x5F, 0x1A, 0x56, 0xD6, 0x01, 0xA9, 0xA4, 0x44, 0x03, 0x77, 0xB8, 0x0C, 0xF7, 0xF0 },
                { 0x85, 0x09, 0xE1, 0x8D, 0x1F, 0x56, 0xD2, 0x4B, 0xCB, 0xC3, 0xEF, 0x10, 0x08, 0xE0, 0x36, 0x6A,
                  0xAC, 0x57, 0x7C, 0x94, 0xEC, 0x2D, 0xFE, 0xDF, 0x41, 0x8C, 0x5A, 0xC7, 0x77, 0x21, 0xEA },
                { 0xC4, 0xAF, 0x4B, 0x98, 0xF3, 0xCF, 0x5B, 0x62, 0x1F, 0x23, 0xE5, 0x32, 0x41, 0x27, 0x69, 0x15,
                  0x4F, 0xE2, 0x4D, 0x16, 0xB7, 0xF7, 0xF8, 0x21, 0x72, 0x58, 0xEC, 0xD3, 0x8A, 0xFB, 0x8B }
            }, {
                { 0x36, 0x02, 0x6F, 0xB5, 0x12, 0x94, 0x31, 0x1E, 0x78, 0xF4, 0x07, 0xFD, 0xED, 0x9F, 0xC2, 0x17,
                  0xD0, 0x2C, 0x75, 0xAF, 0x2F, 0x65, 0xE4, 0xB3, 0xA5, 0x38, 0xCF, 0x20, 0x14, 0x91, 0x8A },
                { 0xF0, 0xAE, 0x9C, 0xF8, 0xC5, 0xAD, 0x73, 0x00, 0x55, 0xCD, 0x0F, 0xE5, 0x38, 0x3F, 0x44, 0x6B,
                  0x4E, 0x84, 0xBE, 0x39, 0xEE, 0x88, 0xEB, 0xE8, 0xF9, 0xD8, 0xA8, 0xE2, 0xE9, 0x58, 0x15 },
                { 0x17, 0x8F, 0x1A, 0xD9, 0x94, 0x71, 0xBD, 0x11, 0x47, 0xAD, 0x39, 0x79, 0xC5, 0xC0, 0xEB, 0x09,
                  0x6C, 0x52, 0x63, 0x4E, 0x8D, 0xEA, 0x90, 0xD6, 0x49, 0xCC, 0xD7, 0x88, 0x05, 0x9A, 0xB6 }
            }, {
                { 0xD9, 0x89, 0xAE, 0xD2, 0x1C, 0x0B, 0x95, 0xC0, 0xDF, 0x43, 0x3F, 0x97, 0x27, 0x46, 0xF1, 0x4A,
                  0xDD, 0x13, 0xE6, 0x99, 0xD1, 0xDE, 0x49, 0x41, 0xDB, 0x90, 0xE9, 0x8F, 0xC8, 0xC4, 0x15 },
                { 0x46, 0xAB, 0xF2, 0x42, 0x9E, 0x8F, 0x7D, 0xFB, 0xCE, 0x01, 0x5C, 0x66, 0x0E, 0xA1, 0x81, 0x47,
                  0x04, 0x23, 0xD4, 0xD0, 0x11, 0x97, 0xC0, 0x16, 0xFC, 0x8B, 0xA3, 0xD6, 0xF7, 0x06, 0xB0 },
                { 0x87, 0xE1, 0xDD, 0x0D, 0xBA, 0x3D, 0x08, 0x6A, 0x8C, 0xB5, 0xD4, 0xE7, 0xE6, 0xED, 0xA6, 0xDF,
                  0xA5, 0xA4, 0xCA, 0x6E, 0xA1, 0x51, 0xF9, 0xA2, 0x02, 0x9F, 0x7D, 0xCE, 0x12, 0x82, 0xEE }
            }, {
                { 0xFD, 0xF8, 0x02, 0x5E, 0x92, 0x13, 0x65, 0x57, 0xAF, 0x12, 0x18, 0x25, 0x90, 0xCB, 0xEC, 0x38,
                  0x1B, 0x8B, 0xA5, 0xE7, 0x93, 0x87, 0xB3, 0x46, 0xBB, 0x7E, 0xBF, 0xC4, 0x8F, 0xC0, 0xA4 },
                { 0xF3, 0x3A, 0x57, 0x61, 0x15, 0xB4, 0x1C, 0xDF, 0x5B, 0x91, 0xD1, 0x99, 0x9F, 0x0B, 0xEE, 0x85,
                  0x82, 0x63, 0xAF, 0xA2, 0xC4, 0x0C, 0xB1, 0xA1, 0x33, 0xE3, 0xAE, 0x7B, 0xC1, 0x2B, 0xBE },
                { 0xAE, 0x6B, 0x6A, 0x28, 0x61, 0x75, 0xC9, 0x58, 0x26, 0x0C, 0x52, 0x30, 0x73, 0xDE, 0x70, 0x95,
                  0xDC, 0xE6, 0xAD, 0xDB, 0xB5, 0xDD, 0x63, 0xA5, 0xA7, 0x7A, 0xF1, 0xA2, 0x06, 0xE0, 0xA6 }
            }, {
                { 0xF4, 0x28, 0x09, 0xD7, 0x89, 0xD0, 0xD5, 0x4B, 0x88, 0xF3, 0x7A, 0x01, 0xCA, 0x45, 0x41, 0x61,
                  0xB8, 0xF6, 0x60, 0xD3, 0x74, 0x5C, 0x06, 0xE6, 0x55, 0xAE, 0xDC, 0x54, 0xC7, 0x04, 0x1E },
                { 0xDC, 0x07, 0xFE, 0x6C, 0xB2, 0xBF, 0x83, 0x19, 0x4E, 0x4F, 0x78, 0x87, 0xEA, 0xC5, 0xB8, 0x30,
                  0xED, 0x86, 0xDD, 0x24, 0x36, 0xBD, 0x96, 0x49, 0x01, 0x81, 0x22, 0x3B, 0x94, 0xA4, 0xD5 },
                { 0x59, 0x36, 0xAF, 0x41, 0x6F, 0xD9, 0x37, 0xEC, 0xAA, 0x50, 0x57, 0xE9, 0x4E, 0xC5, 0x32, 0xB9,
                  0x3B, 0x21, 0xF3, 0x38, 0x8D, 0xCD, 0xF5, 0x4D, 0xC1, 0x85, 0x39, 0x84, 0x3C, 0x90, 0x78 },
            }, {
                { 0x51, 0x4D, 0x98, 0xB2, 0xA7, 0x4A, 0x4C, 0x3A, 0x80, 0xAD, 0x2C, 0x91, 0x53, 0xD6, 0xBA, 0x77,
                  0x97, 0xDF, 0xFF, 0xE8, 0x34, 0x82, 0x3B, 0x27, 0x59, 0xD4, 0x5B, 0x4E, 0x6B, 0x58, 0x35 },
                { 0x9E, 0x7E, 0x95, 0x5C, 0xE5, 0xFF, 0xA6, 0x73, 0x14, 0x8C, 0x71, 0x34, 0xC7, 0x9D, 0x46, 0x13,
                  0x29, 0x20, 0xCB, 0xD0, 0xA8, 0x3C, 0xFA, 0x64, 0x59, 0x76, 0xC0, 0xCD, 0xA9, 0xC9, 0x6B },
                { 0xC2, 0xE2, 0x64, 0x40, 0x2A, 0xFF, 0x83, 0xBA, 0xD8, 0x47, 0x17, 0x6E, 0x18, 0xE4, 0x23, 0x42,
                  0xB8, 0x74, 0x5B, 0x54, 0x48, 0x43, 0x77, 0xE3, 0xA1, 0x27, 0x81, 0xAC, 0xC6, 0x1D, 0x01 }
            }, {
                { 0x4F, 0x48, 0x9B, 0xEF, 0x63, 0x9E, 0xB0, 0x9F, 0xF5, 0xF0, 0x7C, 0xC6, 0xBD, 0xA3, 0x72, 0x50,
                  0xED, 0x0F, 0xFE, 0x83, 0x1A, 0xB7, 0xDD, 0x30, 0x33, 0x2A, 0x8A, 0x49, 0x26, 0x56, 0x95 },
                { 0x56, 0x38, 0x2D, 0x9C, 0x6D, 0xA5, 0xA3, 0x53, 0x5A, 0xB7, 0x2E, 0x90, 0x37, 0x70, 0x27, 0xB0,
                  0x32, 0xEF, 0x55, 0x5E, 0xFD, 0x48, 0xC2, 0xE0, 0x18, 0xCE, 0xB3, 0x4B, 0xBB, 0xD6, 0x54 },
                { 0xFE, 0xCA, 0x34, 0xF4, 0x4A, 0x03, 0x1E, 0x9F, 0x97, 0xF9, 0x15, 0x53, 0x46, 0x87, 0x99, 0x24,
                  0x3A, 0x8A, 0xC7, 0x1B, 0x3F, 0x79, 0x31, 0x9A, 0x9B, 0xBE, 0x0E, 0x86, 0x5D, 0x07, 0xD4 }
            }
        };
    }

    struct ErpKey
    {
        int     m_value;
        byte    m_increment;

        public byte Value { get { return (byte)m_value; } }

        public ErpKey (int init_value, byte increment)
        {
            m_value = init_value;
            m_increment = increment;
        }

        public void Increment ()
        {
            m_value += m_increment;
            if (m_value > 0xFF)
                m_value -= 0xFF;
        }
    }
}
