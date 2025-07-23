using System;
using System.ComponentModel.Composition;
using System.IO;
using Concentus.Oggfile;
using Concentus.Structs;

namespace GameRes.Formats.Opus
{
    [Export(typeof(AudioFormat))]
    public class OpusAudio : AudioFormat
    {
        public override string         Tag { get { return "OPUS"; } }
        public override string Description { get { return "Ogg/Opus audio format"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            if (file.Signature != 0x5367674F) // 'OggS'
                return null;
            var header = file.ReadHeader (0x1C);
            int table_size = header[0x1A];
            if (table_size < 1)
                return null;
            int header_size = header[0x1B];
            if (header_size < 0x10)
                return null;
            int header_pos = 0x1B + table_size;
            header = file.ReadHeader (header_pos + header_size);
            if (!header.AsciiEqual (header_pos, "OpusHead"))
                return null;
            int channels = header[header_pos+9];
//            int rate = header.ToInt32 (header_pos+0xC);
            int rate = 48000;
            file.Position = 0;
            var decoder = OpusDecoder.Create (rate, channels);
            var ogg_in = new OpusOggReadStream (decoder, file.AsStream);
            var pcm = new MemoryStream();
            try
            {
                using (var output = new BinaryWriter (pcm, System.Text.Encoding.UTF8, true))
                {
                    while (ogg_in.HasNextPacket)
                    {
                        var packet = ogg_in.DecodeNextPacket();
                        if (packet != null)
                        {
                            for (int i = 0; i < packet.Length; ++i)
                                output.Write (packet[i]);
                        }
                    }
                }
                var format = new WaveFormat
                {
                    FormatTag = 1,
                    Channels = (ushort)channels,
                    SamplesPerSecond = (uint)rate,
                    BitsPerSample = 16,
                };
                format.BlockAlign = (ushort)(format.Channels*format.BitsPerSample/8);
                format.AverageBytesPerSecond = format.SamplesPerSecond*format.BlockAlign;
                pcm.Position = 0;
                var sound = new RawPcmInput (pcm, format);
                file.Dispose();
                return sound;
            }
            catch
            {
                pcm.Dispose();
                throw;
            }
        }
    }
}
