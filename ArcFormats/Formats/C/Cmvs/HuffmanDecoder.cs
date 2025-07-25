using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.Purple
{
    internal sealed class HuffmanDecoder
    {
        byte[]          m_input;
        byte[]          m_output;

        int             m_src;
        int             m_bits;
        int             m_bit_count;

        ushort[] lhs = new ushort[512];
        ushort[] rhs = new ushort[512];
        ushort token = 256;

        public HuffmanDecoder (byte[] src, int index, int length, byte[] dst)
        {
            m_input = src;
            m_output = dst;

            m_src = index;
            m_bit_count = 0;
        }

        public byte[] Unpack ()
        {
            int dst = 0;
            token = 256;
            ushort root = CreateTree();
            while (dst < m_output.Length)
            {
                ushort symbol = root;
                while (symbol >= 0x100)
                {
                    if (0 != GetBits (1))
                        symbol = rhs[symbol];
                    else
                        symbol = lhs[symbol];
                }
                m_output[dst++] = (byte)symbol;
            }
            return m_output;
        }

        ushort CreateTree()
        {
            if (0 != GetBits (1))
            {
                ushort v = token++;
                if (v >= 511)
                    throw new InvalidDataException ("Invalid compressed data");
                lhs[v] =  CreateTree();
                rhs[v] =  CreateTree();
                return v;
            }
            else
            {
                return (ushort)GetBits (8);
            }
        }

        int GetBits (int count)
        {
            int bits = 0;
            while (count --> 0)
            {
                if (0 == m_bit_count)
                {
                    m_bits = LittleEndian.ToInt32 (m_input, m_src);
                    m_src += 4;
                    m_bit_count = 32;
                }
                bits = bits << 1 | (m_bits & 1);
                m_bits >>= 1;
                --m_bit_count;
            }
            return bits;
        }
    }
}
