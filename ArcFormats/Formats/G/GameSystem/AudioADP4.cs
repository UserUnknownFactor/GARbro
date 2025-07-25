using System;
using System.ComponentModel.Composition;
using System.IO;
using GameRes.Utility;

namespace GameRes.Formats.GameSystem
{
    [Export(typeof(AudioFormat))]
    public class Adp4Audio : AudioFormat
    {
        public override string         Tag { get { return "ADP4"; } }
        public override string Description { get { return "'GameSystem' compressed audio"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        public Adp4Audio ()
        {
            Extensions = new string[] { "adp4", "adps" };
        }

        const uint DefaultSampleRate = 44100; // XXX varies

        public override SoundInput TryOpen (IBinaryStream file)
        {
            bool is_adp4 = file.Name.HasExtension (".adp4");
            bool is_adps = !is_adp4 && file.Name.HasExtension (".adps");
            if (!(is_adp4 || is_adps) || file.Length <= 4)
                return null;
            var decoder = new AdpDecoder (file);
            var pcm = decoder.Decode (is_adps);
            var format = new WaveFormat {
                FormatTag      = 1,
                Channels       = 2,
                SamplesPerSecond = DefaultSampleRate,
                BlockAlign     = 4,
                BitsPerSample  = 16,
            };
            format.SetBPS();
            var input = new MemoryStream (pcm);
            var sound = new RawPcmInput (input, format);
            file.Dispose();
            return sound;
        }
    }

    internal sealed class AdpDecoder
    {
        IBinaryStream   m_input;

        public AdpDecoder (IBinaryStream input)
        {
            m_input = input;
        }

        byte[] m_output;

        public byte[] Decode (bool is_adps)
        {
            m_input.Position = 0;
            int sample_count = m_input.ReadInt32();
            if (is_adps)
            {
                m_input.Position = sample_count + 8;
                sample_count = m_input.ReadInt32();
            }
            m_output = new byte[sample_count * 8];
            if (is_adps)
                DecodeAdps (sample_count);
            else
                DecodeBits (sample_count);
            return m_output;
        }

        void DecodeAdps (int sample_count)
        {
            uint sample = 0;
            uint left_sample = 0;
            uint right_sample = 0;
            int dst = 0;
            while (sample_count > 0)
            {
                uint bits = m_input.ReadUInt16();

                left_sample += bits & 0xF;
                int s = AdpSamples[left_sample] + (short)sample;
                sample = sample >> 16 | (uint)(Clamp (s) << 16);
                left_sample = AdpAdjust[left_sample];
                right_sample += (bits >> 8) & 0xF;
                s = AdpSamples[right_sample] + (short)sample;
                sample = sample >> 16 | (uint)(Clamp (s) << 16);
                right_sample = AdpAdjust[right_sample];
                LittleEndian.Pack (sample, m_output, dst);
                dst += 4;

                left_sample += (bits >> 4) & 0xF;
                s = AdpSamples[left_sample] + (short)sample;
                sample = sample >> 16 | (uint)(Clamp (s) << 16);
                left_sample = AdpAdjust[left_sample];
                right_sample += bits >> 12;
                s = AdpSamples[right_sample] + (short)sample;
                sample = sample >> 16 | (uint)(Clamp (s) << 16);
                right_sample = AdpAdjust[right_sample];
                LittleEndian.Pack (sample, m_output, dst);
                dst += 4;
                sample_count -= 2;
            }
        }

        void DecodeBits (int sample_count)
        {
            bool copy_stereo = true;
            uint last_sample = 0;
            short sample = 0;
            uint key = 0;
            int dst = 0;
            int bits = 0;
            while (dst < m_output.Length)
            {
                if (bits < 0x100)
                {
                    bits = m_input.ReadUInt8() | 0x8000;
                }
                int count = m_input.ReadUInt8();
                if (count < 0x80)
                    count = m_input.ReadUInt8() | count << 8;
                else
                    count &= 0x7F;
                count++;
                if (0 == (bits & 1))
                {
                    do
                    {
                        if (dst >= m_output.Length)
                            return;
                        uint data_bits = m_input.ReadUInt8() ^ key++;

                        last_sample += data_bits & 0xF;
                        int s = AdpSamples[last_sample] + sample;
                        last_sample = AdpAdjust[last_sample];
                        sample = Clamp (s);
                        LittleEndian.Pack (sample, m_output, dst);
                        dst += 2;
                        if (copy_stereo)
                            LittleEndian.Pack (sample, m_output, dst);
                        dst += 2;

                        last_sample += (data_bits >> 4) & 0xF;
                        s = AdpSamples[last_sample] + sample;
                        last_sample = AdpAdjust[last_sample];
                        sample = Clamp (s);
                        LittleEndian.Pack (sample, m_output, dst);
                        dst += 2;
                        if (copy_stereo)
                            LittleEndian.Pack (sample, m_output, dst);
                        dst += 2;
                    }
                    while (--count > 0);
                }
                else
                {
                    last_sample = 0;
                    sample = 0;
                    dst += count * 8;
                }
                bits >>= 1;
            }
        }

        static short Clamp (int sample)
        {
            if (sample > 0x7FFF)
                sample = 0x7FFF;
            else if (sample < -32768)
                sample = -32768;
            return (short)sample;
        }

        static readonly int[] AdpSamples = {
            0,     2,     4,     6,     7,     9,    11,    13,     0,    -2,    -4,    -6,    -7,    -9,   -11,   -13,
            1,     3,     5,     7,     9,    11,    13,    15,    -1,    -3,    -5,    -7,    -9,   -11,   -13,   -15,
            1,     3,     5,     7,    10,    12,    14,    16,    -1,    -3,    -5,    -7,   -10,   -12,   -14,   -16,
            1,     3,     6,     8,    11,    13,    16,    18,    -1,    -3,    -6,    -8,   -11,   -13,   -16,   -18,
            1,     4,     6,     9,    12,    15,    17,    20,    -1,    -4,    -6,    -9,   -12,   -15,   -17,   -20,
            1,     4,     7,    10,    13,    16,    19,    22,    -1,    -4,    -7,   -10,   -13,   -16,   -19,   -22,
            1,     4,     8,    11,    14,    17,    21,    24,    -1,    -4,    -8,   -11,   -14,   -17,   -21,   -24,
            1,     5,     8,    12,    15,    19,    22,    26,    -1,    -5,    -8,   -12,   -15,   -19,   -22,   -26,
            2,     6,    10,    14,    18,    22,    26,    30,    -2,    -6,   -10,   -14,   -18,   -22,   -26,   -30,
            2,     6,    10,    14,    19,    23,    27,    31,    -2,    -6,   -10,   -14,   -19,   -23,   -27,   -31,
            2,     7,    11,    16,    21,    26,    30,    35,    -2,    -7,   -11,   -16,   -21,   -26,   -30,   -35,
            2,     7,    13,    18,    23,    28,    34,    39,    -2,    -7,   -13,   -18,   -23,   -28,   -34,   -39,
            2,     8,    14,    20,    25,    31,    37,    43,    -2,    -8,   -14,   -20,   -25,   -31,   -37,   -43,
            3,     9,    15,    21,    28,    34,    40,    46,    -3,    -9,   -15,   -21,   -28,   -34,   -40,   -46,
            3,    10,    17,    24,    31,    38,    45,    52,    -3,   -10,   -17,   -24,   -31,   -38,   -45,   -52,
            3,    11,    19,    27,    34,    42,    50,    58,    -3,   -11,   -19,   -27,   -34,   -42,   -50,   -58,
            4,    12,    21,    29,    38,    46,    55,    63,    -4,   -12,   -21,   -29,   -38,   -46,   -55,   -63,
            4,    13,    23,    32,    41,    50,    60,    69,    -4,   -13,   -23,   -32,   -41,   -50,   -60,   -69,
            5,    15,    25,    35,    46,    56,    66,    76,    -5,   -15,   -25,   -35,   -46,   -56,   -66,   -76,
            5,    16,    28,    39,    50,    61,    73,    84,    -5,   -16,   -28,   -39,   -50,   -61,   -73,   -84,
            6,    18,    31,    43,    56,    68,    81,    93,    -6,   -18,   -31,   -43,   -56,   -68,   -81,   -93,
            6,    20,    34,    48,    61,    75,    89,   103,    -6,   -20,   -34,   -48,   -61,   -75,   -89,  -103,
            7,    22,    37,    52,    67,    82,    97,   112,    -7,   -22,   -37,   -52,   -67,   -82,   -97,  -112,
            8,    24,    41,    57,    74,    90,   107,   123,    -8,   -24,   -41,   -57,   -74,   -90,  -107,  -123,
            9,    27,    45,    63,    82,   100,   118,   136,    -9,   -27,   -45,   -63,   -82,  -100,  -118,  -136,
            10,    30,    50,    70,    90,   110,   130,   150,   -10,   -30,   -50,   -70,   -90,  -110,  -130,  -150,
            11,    33,    55,    77,    99,   121,   143,   165,   -11,   -33,   -55,   -77,   -99,  -121,  -143,  -165,
            12,    36,    60,    84,   109,   133,   157,   181,   -12,   -36,   -60,   -84,  -109,  -133,  -157,  -181,
            13,    40,    66,    93,   120,   147,   173,   200,   -13,   -40,   -66,   -93,  -120,  -147,  -173,  -200,
            14,    44,    73,   103,   132,   162,   191,   221,   -14,   -44,   -73,  -103,  -132,  -162,  -191,  -221,
            16,    48,    81,   113,   146,   178,   211,   243,   -16,   -48,   -81,  -113,  -146,  -178,  -211,  -243,
            17,    53,    89,   125,   160,   196,   232,   268,   -17,   -53,   -89,  -125,  -160,  -196,  -232,  -268,
            19,    58,    98,   137,   176,   215,   255,   294,   -19,   -58,   -98,  -137,  -176,  -215,  -255,  -294,
            21,    64,   108,   151,   194,   237,   281,   324,   -21,   -64,  -108,  -151,  -194,  -237,  -281,  -324,
            23,    71,   118,   166,   213,   261,   308,   356,   -23,   -71,  -118,  -166,  -213,  -261,  -308,  -356,
            26,    78,   130,   182,   235,   287,   339,   391,   -26,   -78,  -130,  -182,  -235,  -287,  -339,  -391,
            28,    86,   143,   201,   258,   316,   373,   431,   -28,   -86,  -143,  -201,  -258,  -316,  -373,  -431,
            31,    94,   158,   221,   284,   347,   411,   474,   -31,   -94,  -158,  -221,  -284,  -347,  -411,  -474,
            34,   104,   174,   244,   313,   383,   453,   523,   -34,  -104,  -174,  -244,  -313,  -383,  -453,  -523,
            38,   115,   191,   268,   345,   422,   498,   575,   -38,  -115,  -191,  -268,  -345,  -422,  -498,  -575,
            42,   126,   210,   294,   379,   463,   547,   631,   -42,  -126,  -210,  -294,  -379,  -463,  -547,  -631,
            46,   139,   231,   324,   417,   510,   602,   695,   -46,  -139,  -231,  -324,  -417,  -510,  -602,  -695,
            51,   153,   255,   357,   459,   561,   663,   765,   -51,  -153,  -255,  -357,  -459,  -561,  -663,  -765,
            56,   168,   280,   392,   505,   617,   729,   841,   -56,  -168,  -280,  -392,  -505,  -617,  -729,  -841,
            61,   185,   308,   432,   555,   679,   802,   926,   -61,  -185,  -308,  -432,  -555,  -679,  -802,  -926,
            68,   204,   340,   476,   612,   748,   884,  1020,   -68,  -204,  -340,  -476,  -612,  -748,  -884, -1020,
            74,   224,   373,   523,   672,   822,   971,  1121,   -74,  -224,  -373,  -523,  -672,  -822,  -971, -1121,
            82,   246,   411,   575,   740,   904,  1069,  1233,   -82,  -246,  -411,  -575,  -740,  -904, -1069, -1233,
            90,   271,   452,   633,   814,   995,  1176,  1357,   -90,  -271,  -452,  -633,  -814,  -995, -1176, -1357,
            99,   298,   497,   696,   895,  1094,  1293,  1492,   -99,  -298,  -497,  -696,  -895, -1094, -1293, -1492,
            109,   328,   547,   766,   985,  1204,  1423,  1642,  -109,  -328,  -547,  -766,  -985, -1204, -1423, -1642,
            120,   361,   601,   842,  1083,  1324,  1564,  1805,  -120,  -361,  -601,  -842, -1083, -1324, -1564, -1805,
            132,   397,   662,   927,  1192,  1457,  1722,  1987,  -132,  -397,  -662,  -927, -1192, -1457, -1722, -1987,
            145,   437,   728,  1020,  1311,  1603,  1894,  2186,  -145,  -437,  -728, -1020, -1311, -1603, -1894, -2186,
            160,   480,   801,  1121,  1442,  1762,  2083,  2403,  -160,  -480,  -801, -1121, -1442, -1762, -2083, -2403,
            176,   529,   881,  1234,  1587,  1940,  2292,  2645,  -176,  -529,  -881, -1234, -1587, -1940, -2292, -2645,
            194,   582,   970,  1358,  1746,  2134,  2522,  2910,  -194,  -582,  -970, -1358, -1746, -2134, -2522, -2910,
            213,   640,  1066,  1493,  1920,  2347,  2773,  3200,  -213,  -640, -1066, -1493, -1920, -2347, -2773, -3200,
            234,   704,  1173,  1643,  2112,  2582,  3051,  3521,  -234,  -704, -1173, -1643, -2112, -2582, -3051, -3521,
            258,   774,  1291,  1807,  2324,  2840,  3357,  3873,  -258,  -774, -1291, -1807, -2324, -2840, -3357, -3873,
            284,   852,  1420,  1988,  2556,  3124,  3692,  4260,  -284,  -852, -1420, -1988, -2556, -3124, -3692, -4260,
            312,   937,  1561,  2186,  2811,  3436,  4060,  4685,  -312,  -937, -1561, -2186, -2811, -3436, -4060, -4685,
            343,  1030,  1718,  2405,  3092,  3779,  4467,  5154,  -343, -1030, -1718, -2405, -3092, -3779, -4467, -5154,
            378,  1134,  1890,  2646,  3402,  4158,  4914,  5670,  -378, -1134, -1890, -2646, -3402, -4158, -4914, -5670,
            415,  1247,  2079,  2911,  3742,  4574,  5406,  6238,  -415, -1247, -2079, -2911, -3742, -4574, -5406, -6238,
            457,  1372,  2287,  3202,  4117,  5032,  5947,  6862,  -457, -1372, -2287, -3202, -4117, -5032, -5947, -6862,
            503,  1509,  2516,  3522,  4529,  5535,  6542,  7548,  -503, -1509, -2516, -3522, -4529, -5535, -6542, -7548,
            553,  1660,  2767,  3874,  4981,  6088,  7195,  8302,  -553, -1660, -2767, -3874, -4981, -6088, -7195, -8302,
            608,  1826,  3044,  4262,  5479,  6697,  7915,  9133,  -608, -1826, -3044, -4262, -5479, -6697, -7915, -9133,
            669,  2009,  3348,  4688,  6027,  7367,  8706, 10046,  -669, -2009, -3348, -4688, -6027, -7367, -8706, -10046,
            736,  2210,  3683,  5157,  6630,  8104,  9577, 11051,  -736, -2210, -3683, -5157, -6630, -8104, -9577, -11051,
            810,  2431,  4052,  5673,  7294,  8915, 10536, 12157,  -810, -2431, -4052, -5673, -7294, -8915, -10536, -12157,
            891,  2674,  4457,  6240,  8023,  9806, 11589, 13372,  -891, -2674, -4457, -6240, -8023, -9806, -11589, -13372,
            980,  2941,  4903,  6864,  8825, 10786, 12748, 14709,  -980, -2941, -4903, -6864, -8825, -10786, -12748, -14709,
            1078,  3236,  5393,  7551,  9708, 11866, 14023, 16181, -1078, -3236, -5393, -7551, -9708, -11866, -14023, -16181,
            1186,  3559,  5933,  8306, 10679, 13052, 15426, 17799, -1186, -3559, -5933, -8306, -10679, -13052, -15426, -17799,
            1305,  3915,  6526,  9136, 11747, 14357, 16968, 19578, -1305, -3915, -6526, -9136, -11747, -14357, -16968, -19578,
            1435,  4307,  7179, 10051, 12922, 15794, 18666, 21538, -1435, -4307, -7179, -10051, -12922, -15794, -18666, -21538,
            1579,  4738,  7896, 11055, 14214, 17373, 20531, 23690, -1579, -4738, -7896, -11055, -14214, -17373, -20531, -23690,
            1737,  5212,  8686, 12161, 15636, 19111, 22585, 26060, -1737, -5212, -8686, -12161, -15636, -19111, -22585, -26060,
            1911,  5733,  9555, 13377, 17200, 21022, 24844, 28666, -1911, -5733, -9555, -13377, -17200, -21022, -24844, -28666,
            2102,  6306, 10511, 14715, 18920, 23124, 27329, 31533, -2102, -6306, -10511, -14715, -18920, -23124, -27329, -31533,
            2312,  6937, 11562, 16187, 20812, 25437, 30062, 34687, -2312, -6937, -11562, -16187, -20812, -25437, -30062, -34687,
            2543,  7631, 12718, 17806, 22893, 27981, 33068, 38156, -2543, -7631, -12718, -17806, -22893, -27981, -33068, -38156,
            2798,  8394, 13990, 19586, 25183, 30779, 36375, 41971, -2798, -8394, -13990, -19586, -25183, -30779, -36375, -41971,
            3077,  9233, 15389, 21545, 27700, 33856, 40012, 46168, -3077, -9233, -15389, -21545, -27700, -33856, -40012, -46168,
            3385, 10157, 16928, 23700, 30471, 37243, 44014, 50786, -3385, -10157, -16928, -23700, -30471, -37243, -44014, -50786,
            3724, 11172, 18621, 26069, 33518, 40966, 48415, 55863, -3724, -11172, -18621, -26069, -33518, -40966, -48415, -55863,
        };
        static readonly ushort[] AdpAdjust = {
            0,     0,     0,     0,    32,    64,    96,   128,     0,     0,     0,     0,    32,    64,    96,   128,
            0,     0,     0,     0,    48,    80,   112,   144,     0,     0,     0,     0,    48,    80,   112,   144,
            16,    16,    16,    16,    64,    96,   128,   160,    16,    16,    16,    16,    64,    96,   128,   160,
            32,    32,    32,    32,    80,   112,   144,   176,    32,    32,    32,    32,    80,   112,   144,   176,
            48,    48,    48,    48,    96,   128,   160,   192,    48,    48,    48,    48,    96,   128,   160,   192,
            64,    64,    64,    64,   112,   144,   176,   208,    64,    64,    64,    64,   112,   144,   176,   208,
            80,    80,    80,    80,   128,   160,   192,   224,    80,    80,    80,    80,   128,   160,   192,   224,
            96,    96,    96,    96,   144,   176,   208,   240,    96,    96,    96,    96,   144,   176,   208,   240,
            112,   112,   112,   112,   160,   192,   224,   256,   112,   112,   112,   112,   160,   192,   224,   256,
            128,   128,   128,   128,   176,   208,   240,   272,   128,   128,   128,   128,   176,   208,   240,   272,
            144,   144,   144,   144,   192,   224,   256,   288,   144,   144,   144,   144,   192,   224,   256,   288,
            160,   160,   160,   160,   208,   240,   272,   304,   160,   160,   160,   160,   208,   240,   272,   304,
            176,   176,   176,   176,   224,   256,   288,   320,   176,   176,   176,   176,   224,   256,   288,   320,
            192,   192,   192,   192,   240,   272,   304,   336,   192,   192,   192,   192,   240,   272,   304,   336,
            208,   208,   208,   208,   256,   288,   320,   352,   208,   208,   208,   208,   256,   288,   320,   352,
            224,   224,   224,   224,   272,   304,   336,   368,   224,   224,   224,   224,   272,   304,   336,   368,
            240,   240,   240,   240,   288,   320,   352,   384,   240,   240,   240,   240,   288,   320,   352,   384,
            256,   256,   256,   256,   304,   336,   368,   400,   256,   256,   256,   256,   304,   336,   368,   400,
            272,   272,   272,   272,   320,   352,   384,   416,   272,   272,   272,   272,   320,   352,   384,   416,
            288,   288,   288,   288,   336,   368,   400,   432,   288,   288,   288,   288,   336,   368,   400,   432,
            304,   304,   304,   304,   352,   384,   416,   448,   304,   304,   304,   304,   352,   384,   416,   448,
            320,   320,   320,   320,   368,   400,   432,   464,   320,   320,   320,   320,   368,   400,   432,   464,
            336,   336,   336,   336,   384,   416,   448,   480,   336,   336,   336,   336,   384,   416,   448,   480,
            352,   352,   352,   352,   400,   432,   464,   496,   352,   352,   352,   352,   400,   432,   464,   496,
            368,   368,   368,   368,   416,   448,   480,   512,   368,   368,   368,   368,   416,   448,   480,   512,
            384,   384,   384,   384,   432,   464,   496,   528,   384,   384,   384,   384,   432,   464,   496,   528,
            400,   400,   400,   400,   448,   480,   512,   544,   400,   400,   400,   400,   448,   480,   512,   544,
            416,   416,   416,   416,   464,   496,   528,   560,   416,   416,   416,   416,   464,   496,   528,   560,
            432,   432,   432,   432,   480,   512,   544,   576,   432,   432,   432,   432,   480,   512,   544,   576,
            448,   448,   448,   448,   496,   528,   560,   592,   448,   448,   448,   448,   496,   528,   560,   592,
            464,   464,   464,   464,   512,   544,   576,   608,   464,   464,   464,   464,   512,   544,   576,   608,
            480,   480,   480,   480,   528,   560,   592,   624,   480,   480,   480,   480,   528,   560,   592,   624,
            496,   496,   496,   496,   544,   576,   608,   640,   496,   496,   496,   496,   544,   576,   608,   640,
            512,   512,   512,   512,   560,   592,   624,   656,   512,   512,   512,   512,   560,   592,   624,   656,
            528,   528,   528,   528,   576,   608,   640,   672,   528,   528,   528,   528,   576,   608,   640,   672,
            544,   544,   544,   544,   592,   624,   656,   688,   544,   544,   544,   544,   592,   624,   656,   688,
            560,   560,   560,   560,   608,   640,   672,   704,   560,   560,   560,   560,   608,   640,   672,   704,
            576,   576,   576,   576,   624,   656,   688,   720,   576,   576,   576,   576,   624,   656,   688,   720,
            592,   592,   592,   592,   640,   672,   704,   736,   592,   592,   592,   592,   640,   672,   704,   736,
            608,   608,   608,   608,   656,   688,   720,   752,   608,   608,   608,   608,   656,   688,   720,   752,
            624,   624,   624,   624,   672,   704,   736,   768,   624,   624,   624,   624,   672,   704,   736,   768,
            640,   640,   640,   640,   688,   720,   752,   784,   640,   640,   640,   640,   688,   720,   752,   784,
            656,   656,   656,   656,   704,   736,   768,   800,   656,   656,   656,   656,   704,   736,   768,   800,
            672,   672,   672,   672,   720,   752,   784,   816,   672,   672,   672,   672,   720,   752,   784,   816,
            688,   688,   688,   688,   736,   768,   800,   832,   688,   688,   688,   688,   736,   768,   800,   832,
            704,   704,   704,   704,   752,   784,   816,   848,   704,   704,   704,   704,   752,   784,   816,   848,
            720,   720,   720,   720,   768,   800,   832,   864,   720,   720,   720,   720,   768,   800,   832,   864,
            736,   736,   736,   736,   784,   816,   848,   880,   736,   736,   736,   736,   784,   816,   848,   880,
            752,   752,   752,   752,   800,   832,   864,   896,   752,   752,   752,   752,   800,   832,   864,   896,
            768,   768,   768,   768,   816,   848,   880,   912,   768,   768,   768,   768,   816,   848,   880,   912,
            784,   784,   784,   784,   832,   864,   896,   928,   784,   784,   784,   784,   832,   864,   896,   928,
            800,   800,   800,   800,   848,   880,   912,   944,   800,   800,   800,   800,   848,   880,   912,   944,
            816,   816,   816,   816,   864,   896,   928,   960,   816,   816,   816,   816,   864,   896,   928,   960,
            832,   832,   832,   832,   880,   912,   944,   976,   832,   832,   832,   832,   880,   912,   944,   976,
            848,   848,   848,   848,   896,   928,   960,   992,   848,   848,   848,   848,   896,   928,   960,   992,
            864,   864,   864,   864,   912,   944,   976,  1008,   864,   864,   864,   864,   912,   944,   976,  1008,
            880,   880,   880,   880,   928,   960,   992,  1024,   880,   880,   880,   880,   928,   960,   992,  1024,
            896,   896,   896,   896,   944,   976,  1008,  1040,   896,   896,   896,   896,   944,   976,  1008,  1040,
            912,   912,   912,   912,   960,   992,  1024,  1056,   912,   912,   912,   912,   960,   992,  1024,  1056,
            928,   928,   928,   928,   976,  1008,  1040,  1072,   928,   928,   928,   928,   976,  1008,  1040,  1072,
            944,   944,   944,   944,   992,  1024,  1056,  1088,   944,   944,   944,   944,   992,  1024,  1056,  1088,
            960,   960,   960,   960,  1008,  1040,  1072,  1104,   960,   960,   960,   960,  1008,  1040,  1072,  1104,
            976,   976,   976,   976,  1024,  1056,  1088,  1120,   976,   976,   976,   976,  1024,  1056,  1088,  1120,
            992,   992,   992,   992,  1040,  1072,  1104,  1136,   992,   992,   992,   992,  1040,  1072,  1104,  1136,
            1008,  1008,  1008,  1008,  1056,  1088,  1120,  1152,  1008,  1008,  1008,  1008,  1056,  1088,  1120,  1152,
            1024,  1024,  1024,  1024,  1072,  1104,  1136,  1168,  1024,  1024,  1024,  1024,  1072,  1104,  1136,  1168,
            1040,  1040,  1040,  1040,  1088,  1120,  1152,  1184,  1040,  1040,  1040,  1040,  1088,  1120,  1152,  1184,
            1056,  1056,  1056,  1056,  1104,  1136,  1168,  1200,  1056,  1056,  1056,  1056,  1104,  1136,  1168,  1200,
            1072,  1072,  1072,  1072,  1120,  1152,  1184,  1216,  1072,  1072,  1072,  1072,  1120,  1152,  1184,  1216,
            1088,  1088,  1088,  1088,  1136,  1168,  1200,  1232,  1088,  1088,  1088,  1088,  1136,  1168,  1200,  1232,
            1104,  1104,  1104,  1104,  1152,  1184,  1216,  1248,  1104,  1104,  1104,  1104,  1152,  1184,  1216,  1248,
            1120,  1120,  1120,  1120,  1168,  1200,  1232,  1264,  1120,  1120,  1120,  1120,  1168,  1200,  1232,  1264,
            1136,  1136,  1136,  1136,  1184,  1216,  1248,  1280,  1136,  1136,  1136,  1136,  1184,  1216,  1248,  1280,
            1152,  1152,  1152,  1152,  1200,  1232,  1264,  1296,  1152,  1152,  1152,  1152,  1200,  1232,  1264,  1296,
            1168,  1168,  1168,  1168,  1216,  1248,  1280,  1312,  1168,  1168,  1168,  1168,  1216,  1248,  1280,  1312,
            1184,  1184,  1184,  1184,  1232,  1264,  1296,  1328,  1184,  1184,  1184,  1184,  1232,  1264,  1296,  1328,
            1200,  1200,  1200,  1200,  1248,  1280,  1312,  1344,  1200,  1200,  1200,  1200,  1248,  1280,  1312,  1344,
            1216,  1216,  1216,  1216,  1264,  1296,  1328,  1360,  1216,  1216,  1216,  1216,  1264,  1296,  1328,  1360,
            1232,  1232,  1232,  1232,  1280,  1312,  1344,  1376,  1232,  1232,  1232,  1232,  1280,  1312,  1344,  1376,
            1248,  1248,  1248,  1248,  1296,  1328,  1360,  1392,  1248,  1248,  1248,  1248,  1296,  1328,  1360,  1392,
            1264,  1264,  1264,  1264,  1312,  1344,  1376,  1392,  1264,  1264,  1264,  1264,  1312,  1344,  1376,  1392,
            1280,  1280,  1280,  1280,  1328,  1360,  1392,  1392,  1280,  1280,  1280,  1280,  1328,  1360,  1392,  1392,
            1296,  1296,  1296,  1296,  1344,  1376,  1392,  1392,  1296,  1296,  1296,  1296,  1344,  1376,  1392,  1392,
            1312,  1312,  1312,  1312,  1360,  1392,  1392,  1392,  1312,  1312,  1312,  1312,  1360,  1392,  1392,  1392,
            1328,  1328,  1328,  1328,  1376,  1392,  1392,  1392,  1328,  1328,  1328,  1328,  1376,  1392,  1392,  1392,
            1344,  1344,  1344,  1344,  1392,  1392,  1392,  1392,  1344,  1344,  1344,  1344,  1392,  1392,  1392,  1392,
            1360,  1360,  1360,  1360,  1392,  1392,  1392,  1392,  1360,  1360,  1360,  1360,  1392,  1392,  1392,  1392,
            1376,  1376,  1376,  1376,  1392,  1392,  1392,  1392,  1376,  1376,  1376,  1376,  1392,  1392,  1392,  1392,
        };
    }
}
