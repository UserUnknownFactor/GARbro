using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using GameRes.Cryptography;

namespace GameRes.Formats.Dogenzaka
{
    internal class Rc4PngMetaData : ImageMetaData
    {
        public byte[]   Key;
    }

    [Export(typeof(ImageFormat))]
    public class Rc4PngFormat : PngFormat
    {
        public override string         Tag { get { return "PNG/RC4"; } }
        public override string Description { get { return "RC4 encrypted PNG image"; } }
        public override uint     Signature { get { return 0xC4F7F61A; } }
        public override bool      CanWrite { get { return false; } }

        public Rc4PngFormat ()
        {
            Extensions = new string[] { "a" };
        }

        public static readonly byte[] KnownKey = Encoding.ASCII.GetBytes ("Hlk9D28p");

        public override ImageMetaData ReadMetaData (IBinaryStream stream)
        {
            using (var sha = SHA1.Create())
            {
                var key = sha.ComputeHash (KnownKey).Take (16).ToArray();
                using (var proxy = new InputProxyStream (stream.AsStream, true))
                using (var crypto = new InputCryptoStream (proxy, new Rc4Transform (key)))
                using (var input = new BinaryStream (crypto, stream.Name))
                {
                    var info = base.ReadMetaData (input);
                    if (null == info)
                        return null;
                    return new Rc4PngMetaData
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
        }

        public override ImageData Read (IBinaryStream stream, ImageMetaData info)
        {
            var rc4 = (Rc4PngMetaData)info;
            using (var sha = SHA1.Create())
            using (var proxy = new InputProxyStream (stream.AsStream, true))
            using (var crypto = new InputCryptoStream (proxy, new Rc4Transform (rc4.Key)))
            using (var input = new BinaryStream (crypto, stream.Name))
                return base.Read (input, info);
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new NotImplementedException ("Rc4PngFormat.Write not implemented");
        }
    }
}
