using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace GameRes.Formats.Borland
{
    public sealed class DelphiDeserializer
    {
        IBinaryStream   m_input;

        public Encoding Encoding { get; set; }

        public DelphiDeserializer (IBinaryStream input)
        {
            m_input = input;
            Encoding = Encodings.cp932;
        }

        public DelphiObject Deserialize ()
        {
            if (m_input.ReadUInt32() != 0x30465054) // 'TPF0'
                return null;
            return DeserializeNode();
        }

        DelphiObject DeserializeNode ()
        {
            int type_len = m_input.ReadByte();
            if (type_len <= 0)
                return null;
            var node = new DelphiObject();
            node.Type = ReadString (type_len);
            node.Name = ReadString();
            int key_length;
            while ((key_length = m_input.ReadUInt8()) > 0)
            {
                var key = ReadString (key_length);
                node.Props[key] = ReadValue();
            }
            DelphiObject child;
            while ((child = DeserializeNode()) != null)
            {
                node.Contents.Add (child);
            }
            return node;
        }

        object ReadValue ()
        {
            int type = m_input.ReadUInt8();
            switch (type)
            {
            case 2:  return (int)m_input.ReadUInt8();
            case 3:  return (int)m_input.ReadUInt16();
            case 5:  return ReadLongDouble();
            case 6:
            case 7:  return ReadString();
            case 8:
            case 9:  return true;
            case 10: return ReadByteString();
            case 11: return ReadStringArray();
            case 18: return ReadUnicodeString();
            default: throw new System.NotImplementedException();
            }
        }

        string ReadString ()
        {
            return ReadString (m_input.ReadUInt8());
        }

        string ReadString (int length)
        {
            return m_input.ReadCString (length, Encoding);
        }

        string ReadUnicodeString ()
        {
            int length = m_input.ReadInt32();
            if (length < 0)
                throw new InvalidFormatException();
            if (0 == length)
                return "";
            var bytes = m_input.ReadBytes (length * 2);
            return Encoding.Unicode.GetString (bytes);
        }

        byte[] ReadByteString ()
        {
            int length = m_input.ReadInt32();
            if (length < 0)
                throw new InvalidFormatException();
            if (0 == length)
                return new byte[0];
            return m_input.ReadBytes (length);
        }

        IList<string> ReadStringArray ()
        {
            var list = new List<string>();
            int length;
            while ((length = m_input.ReadUInt8()) > 0)
            {
                list.Add (ReadString (length));
            }
            return list;
        }

        object ReadLongDouble ()
        {
            return m_input.ReadBytes (10); // long double deserialization not implemented
        }
    }

    public class DelphiObject
    {
        public string       Type;
        public string       Name;
        public IDictionary  Props = new Hashtable();
        public IList<DelphiObject> Contents = new List<DelphiObject>();
    }
}
