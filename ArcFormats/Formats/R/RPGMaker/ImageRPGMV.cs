using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameRes.Formats.RPGMaker
{
    internal class RpgmvpMetaData : ImageMetaData
    {
        public byte[]   Key;
    }

    [Export(typeof(ImageFormat))]
    public class RpgmvpFormat : ImageFormat
    {
        public override string         Tag { get { return "RPGMVP"; } }
        public override string Description { get { return "RPG Maker MV/MZ engine image format"; } }
        public override uint     Signature { get { return  0x4D475052; } } // 'RPGMV'

        public RpgmvpFormat ()
        {
            Extensions = new string[] { "rpgmvp", "png_" };
        }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (0x14);
            if (header[4] != 'V')
                return null;

            var key = RpgmvDecryptor.LastKey ?? RpgmvDecryptor.FindKeyFor (file.Name);
            if (null == key)
                return null;

            var decrypted_header = new byte[header.Length];
            for (int i = 0; i < 16; ++i)
                decrypted_header[i] ^= key[i];

            using (var header_stream = new BinaryStream(new MemoryStream(decrypted_header), file.Name))
            {
                var im_format = ImageFormat.FindFormat(header_stream);
                if (null == im_format)
                {
                    RpgmvDecryptor.LastKey = null;
                    return null;
                }
            }

            RpgmvDecryptor.LastKey = key;
            using (var png = RpgmvDecryptor.DecryptStream (file, key, true))
            {
                var info = Png.ReadMetaData (png);
                if (null == info)
                    return null;

                return new RpgmvpMetaData
                {
                    Width = info.Width,
                    Height = info.Height,
                    OffsetX = info.OffsetX,
                    OffsetY = info.OffsetY,
                    BPP = info.BPP,
                    Key = key,
                };
            }
        }

        public override ImageData Read(IBinaryStream file, ImageMetaData info)
        {
            var meta = (RpgmvpMetaData)info;
            using (var png = RpgmvDecryptor.DecryptStream(file, meta.Key, true))
                return Png.Read(png, info);
        }

        public override void Write(Stream file, ImageData image)
        {
            byte[] key = RpgmvDecryptor.LastKey ?? RpgmvDecryptor.DefaultKey;

            file.Write(RpgmvDecryptor.DefaultHeader, 0, RpgmvDecryptor.DefaultHeader.Length);

            using (var pngStream = new MemoryStream())
            {
                Png.Write(pngStream, image);
                pngStream.Position = 0;

                var pngHeader = new byte[key.Length];
                pngStream.Read(pngHeader, 0, pngHeader.Length);

                for (int i = 0; i < key.Length; ++i)
                    pngHeader[i] ^= key[i];

                file.Write(pngHeader, 0, pngHeader.Length);

                pngStream.CopyTo(file);
            }
        }
    }

    internal class RpgmvDecryptor
    {
        public static IBinaryStream DecryptStream (IBinaryStream input, byte[] key, bool leave_open = false)
        {
            input.Position = 0x10;
            var header = input.ReadBytes (key.Length);
            for (int i = 0; i < key.Length; ++i)
                header[i] ^= key[i];
            var result = new PrefixStream (header, new StreamRegion (input.AsStream, input.Position, leave_open));
            return new BinaryStream (result, input.Name);
        }

        static byte[] GetKeyFromString (string hex)
        {
            if ((hex.Length & 1) != 0)
                throw new System.ArgumentException ("invalid key string");

            var key = new byte[hex.Length/2];
            for (int i = 0; i < key.Length; ++i)
                key[i] = (byte)(HexToInt (hex[i * 2]) << 4 | HexToInt (hex[i * 2 + 1]));
            return key;
        }

        static int HexToInt (char x)
        {
            if (char.IsDigit (x))
                return x - '0';
            else
                return char.ToUpper (x) - 'A' + 10;
        }

        static byte[] ParseSystemJson (string filename)
        {
            var json = File.ReadAllText (filename, Encoding.UTF8);
            try
            {
                var sys = JObject.Parse(json);
                var key = sys["encryptionKey"]?.Value<string>();
                if (null == key)
                    return null;
                return GetKeyFromString (key);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public static byte[] FindKeyFor (string filename)
        {
            foreach (var system_filename in FindSystemJson (filename))
            {
                if (File.Exists (system_filename))
                    return ParseSystemJson (system_filename);
            }
            return null;
        }

        static IEnumerable<string> FindSystemJson (string filename)
        {
            var dir_name = Path.GetDirectoryName (filename);
            yield return Path.Combine (dir_name, @"..\..\data\System.json");
            yield return Path.Combine (dir_name, @"..\..\..\www\data\System.json");
            yield return Path.Combine (dir_name, @"..\..\..\data\System.json");
            yield return Path.Combine (dir_name, @"..\..\..\..\data\System.json");
            yield return Path.Combine (dir_name, @"..\data\System.json");
            yield return Path.Combine (dir_name, @"data\System.json");
        }

        internal static readonly byte[] DefaultKey = {
            0x77, 0x4E, 0x46, 0x45, 0xFC, 0x43, 0x2F, 0x71, 0x47, 0x95, 0xA2, 0x43, 0xE5, 0x10, 0x13, 0xD8
        };

        internal static readonly byte[] DefaultHeader = {
            0x52, 0x50, 0x47, 0x4D, 0x56, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        };

        internal static byte[] LastKey = null;
    }
}