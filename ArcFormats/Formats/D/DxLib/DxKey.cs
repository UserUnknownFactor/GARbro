using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using GameRes.Utility;

namespace GameRes.Formats.DxLib
{
    public interface IDxKey
    {
        string  Password { get; }
        byte[]       Key { get; }

        byte[] GetEntryKey (string name);
    }

    [Serializable]
    public class DxKey : IDxKey
    {
        string      m_password;
        byte[]      m_key;

        public DxKey () : this (string.Empty)
        {
        }

        public DxKey (string password)
        {
            Password = password;
        }

        public DxKey (byte[] key)
        {
            Key = key;
        }

        public string Password
        {
            get { return m_password; }
            set { m_password = value; m_key = null; }
        }

        public byte[] Key
        {
            get { return m_key ?? (m_key = CreateKey (m_password)); }
            set { m_key = value; m_password = RestoreKey (m_key); }
        }

        public virtual byte[] GetEntryKey (string name)
        {
            return Key;
        }

        protected virtual byte[] CreateKey (string keyword)
        {
            byte[] key;
            if (string.IsNullOrEmpty (keyword))
            {
                key = Enumerable.Repeat<byte> (0xAA, 12).ToArray();
            }
            else
            {
                key = new byte[12];
                int char_count = Math.Min (keyword.Length, 12);
                int length = Encodings.cp932.GetBytes (keyword, 0, char_count, key, 0);
                if (length < 12)
                    Binary.CopyOverlapped (key, 0, length, 12-length);
            }
            key[0] ^= 0xFF;
            key[1]  = Binary.RotByteR (key[1], 4);
            key[2] ^= 0x8A;
            key[3]  = (byte)~Binary.RotByteR (key[3], 4);
            key[4] ^= 0xFF;
            key[5] ^= 0xAC;
            key[6] ^= 0xFF;
            key[7]  = (byte)~Binary.RotByteR (key[7], 3);
            key[8]  = Binary.RotByteL (key[8], 3);
            key[9] ^= 0x7F;
            key[10] = (byte)(Binary.RotByteR (key[10], 4) ^ 0xD6);
            key[11] ^= 0xCC;
            return key;
        }

        protected virtual string RestoreKey (byte[] key)
        {
            var bin = key.Clone() as byte[];
            bin[0] ^= 0xFF;
            bin[1]  = Binary.RotByteL (bin[1], 4);
            bin[2] ^= 0x8A;
            bin[3]  = Binary.RotByteL ((byte)~bin[3], 4);
            bin[4] ^= 0xFF;
            bin[5] ^= 0xAC;
            bin[6] ^= 0xFF;
            bin[7]  = Binary.RotByteL ((byte)~bin[7], 3);
            bin[8]  = Binary.RotByteR (bin[8], 3);
            bin[9] ^= 0x7F;
            bin[10] = Binary.RotByteL ((byte)(bin[10] ^ 0xD6), 4);
            bin[11] ^= 0xCC;
            return Encodings.cp932.GetString (bin);
        }
    }

    [Serializable]
    public class DxKey7 : DxKey
    {
        public DxKey7 (string password) : base (password ?? "DXARC")
        {
        }

        public override byte[] GetEntryKey (string name)
        {
            var password = this.Password;
            var path = name.Split ('\\', '/');
            password += string.Join ("", path.Reverse().Select (n => n.ToUpperInvariant()));
            return CreateKey (password);
        }

        protected override byte[] CreateKey (string keyword)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = Encodings.cp932.GetBytes (keyword);
                return sha.ComputeHash (bytes);
            }
        }

        protected override string RestoreKey (byte[] key)
        {
            throw new NotSupportedException ("SHA-256 key cannot be restored.");
        }
    }

    [Serializable]
    public class DxKey8 : DxKey
    {
        private int codepage;
        public DxKey8(string password,int codepage) : base(password ?? "DXARC")
        {
            this.codepage = codepage;
        }

        public override byte[] GetEntryKey(string name)
        {
            var password = this.Password;
            var path = name.Split('\\', '/');
            password += string.Join("", path.Reverse().Select(n => n.ToUpperInvariant()));
            return CreateKey(password);
        }

        protected override byte[] CreateKey(string keyword)
        {
            //from DxArchive.cpp
            //first check if the keyword is too short
            if (keyword.Length < 4)
            {
                keyword += "DXARC";
            }
            //first split string to bytes. Use original encoding as basis. Otherwise we would fail to decrypt that.
            Encoding tgtEncoding = Encoding.GetEncoding(codepage);
            byte[] tgtBytes = tgtEncoding.GetBytes(keyword);
            byte[] oddBuffer = new byte[(tgtBytes.Length/2)+(tgtBytes.Length%2)]; int oddCounter = 0;
            byte[] evenBuffer = new byte[(tgtBytes.Length/2)]; int evenCounter = 0;
            for (int i=0; i<tgtBytes.Length;i+=2,oddCounter++)
            {
                oddBuffer[oddCounter] = tgtBytes[i];
            }
            for (int i = 1; i < tgtBytes.Length; i += 2, evenCounter++)
            {
                evenBuffer[evenCounter] = tgtBytes[i];
            }
            UInt32 crc_0, crc_1;
            crc_0 = Crc32.Compute(oddBuffer, 0, oddCounter);
            crc_1 = Crc32.Compute(evenBuffer, 0, evenCounter);

            byte[] key = new byte[7];
            byte[] crc_0_Bytes = BitConverter.GetBytes(crc_0),crc_1_Bytes=BitConverter.GetBytes(crc_1);
            key[0] = crc_0_Bytes[0];
            key[1] = crc_0_Bytes[1];
            key[2] = crc_0_Bytes[2];
            key[3] = crc_0_Bytes[3];
            key[4] = crc_1_Bytes[0];
            key[5] = crc_1_Bytes[1];
            key[6] = crc_1_Bytes[2];
            return key;

            /*
            string oddString, evenString;
            oddString = string.Concat(keyword.Where((c, i) => i % 2 == 0));
            evenString = string.Concat(keyword.Where((c, i) => (i+1) % 2 == 0));
            UInt32 crc_0, crc_1;
            crc_0 = Crc32.Compute(Encoding.ASCII.GetBytes(oddString), 0, oddString.Length);
            crc_1 = Crc32.Compute(Encoding.ASCII.GetBytes(evenString), 0, evenString.Length); */
            /*
            using (var sha = SHA256.Create())
            {
                var bytes = Encodings.cp932.GetBytes(keyword);
                return sha.ComputeHash(bytes);
            } */
        }

        protected override string RestoreKey(byte[] key)
        {
            throw new NotSupportedException("CRC key cannot be restored.");
        }
    }
}
