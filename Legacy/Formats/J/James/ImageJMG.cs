using GameRes.Utility;
using System.ComponentModel.Composition;
using System.IO;

// [981105][Berserker] Situation
// [991109][Berserker] Situation 2

namespace GameRes.Formats.James
{
    public enum Obfuscation
    {
        None,
        RotateWords,
        ReverseBits
    }

    public class JmgMetaData : ImageMetaData
    {
        public Obfuscation      Method;
        public ImageMetaData    BmpInfo;
    }

    [Export(typeof(ImageFormat))]
    public class JmgFormat : ImageFormat
    {
        public override string         Tag { get { return "JMG"; } }
        public override string Description { get { return "JAMES engine obfuscated bitmap"; } }
        public override uint     Signature { get { return 0; } }

        public override ImageMetaData ReadMetaData (IBinaryStream file)
        {
            var header = file.ReadHeader (2);
            Obfuscation method;
            if (header[0] == 0xD4 && header[1] == 0x24)
                method = Obfuscation.RotateWords;
            else if (header[0] == 0xB2 && header[1] == 0x42)
                method = Obfuscation.ReverseBits;
            else
                return null;
            using (var input = OpenAsBitmap (file, 0x40, method))
            {
                var info = Bmp.ReadMetaData (input);
                if (null == info)
                    return null;
                return new JmgMetaData
                {
                    Width   = info.Width,
                    Height  = info.Height,
                    BPP     = info.BPP,
                    Method  = method,
                    BmpInfo = info,
                };
            }
        }

        IBinaryStream OpenAsBitmap (IBinaryStream input, int length, Obfuscation method)
        {
            input.Position = 0;
            var data = input.ReadBytes (length);
            Deobfuscate (data, 0, length, method);
            return new BinMemoryStream (data, input.Name);
        }

        public override ImageData Read (IBinaryStream file, ImageMetaData info)
        {
            var meta = (JmgMetaData)info;
            using (var bmp = OpenAsBitmap (file, (int)file.Length, meta.Method))
                return Bmp.Read (bmp, meta.BmpInfo);
        }

        void Deobfuscate (byte[] data, int pos, int length, Obfuscation method)
        {
            switch (method)
            {
            case Obfuscation.RotateWords:   RotateWords (data, pos, length); break;
            case Obfuscation.ReverseBits:   ReverseBits (data, pos, length); break;
            }
        }

        void RotateWords (byte[] data, int pos, int length)
        {
            for (int i = 0; i < length; i += 2)
            {
                ushort w = LittleEndian.ToUInt16 (data, pos+i);
                w = (ushort)(w >> 12 | w << 4);
                LittleEndian.Pack (w, data, pos+i);
            }
        }

        void ReverseBits (byte[] data, int pos, int length)
        {
            for (int i = 0; i < length; i += 2)
            {
                int v = LittleEndian.ToUInt16 (data, pos+i);
                v = (v & 0xAAAA) >> 1 | (v & 0x5555) << 1;
                v = (v & 0xCCCC) >> 2 | (v & 0x3333) << 2;
                v = (v & 0xF0F0) >> 4 | (v & 0x0F0F) << 4;
                v = (v & 0xFF00) >> 8 | (v & 0x00FF) << 8;
                LittleEndian.Pack ((ushort)v, data, pos+i);
            }
        }

        public override void Write (Stream file, ImageData image)
        {
            throw new System.NotImplementedException ("JmgFormat.Write not implemented");
        }
    }
}
