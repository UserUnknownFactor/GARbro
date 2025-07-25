using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using Concentus.Oggfile;
using Concentus.Structs;
using Concentus.Enums;

namespace GameRes.Formats.Opus
{
    public class OpusOptions : ResourceOptions
    {
        public int Bitrate { get; set; }
        public OpusApplication Application { get; set; }

        public OpusOptions()
        {
            Bitrate = 320000;
            Application = OpusApplication.OPUS_APPLICATION_AUDIO;
        }
    }

    [Export(typeof(AudioFormat))]
    public class OpusAudio : AudioFormat
    {
        public override string         Tag { get { return "OPUS"; } }
        public override string Description { get { return "Ogg/Opus audio format"; } }
        public override uint     Signature { get { return  0; } }
        public override bool      CanWrite { get { return  true; } }

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

        public override void Write (SoundInput source, Stream output)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (output == null)
                throw new ArgumentNullException(nameof(output));

            var options = new OpusOptions();

            var pcmSource = source as RawPcmInput;
            if (pcmSource != null)
            {
                WriteOpusStream(pcmSource, output, options);
            }
            else
            {
                // Need to convert to PCM first
                using (var pcmStream = new MemoryStream())
                {
                    source.Position = 0;
                    var buffer = new byte[4096];
                    int read;
                    while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        pcmStream.Write(buffer, 0, read);
                    }

                    pcmStream.Position = 0;
                    using (var tempPcm = new RawPcmInput(pcmStream, source.Format))
                    {
                        WriteOpusStream(tempPcm, output, options);
                    }
                }
            }
        }

        private void WriteOpusStream(RawPcmInput pcmSource, Stream output, OpusOptions options)
        {
            var format = pcmSource.Format;
            const int opusSampleRate = 48000;

            var encoder = OpusEncoder.Create(opusSampleRate, format.Channels, options.Application);
            encoder.Bitrate = Math.Min(options.Bitrate, 510000 * format.Channels);

            var tags = new OpusTags();
            tags.Comment = "Encoded by GARbro";
            var oggOut = new OpusOggWriteStream(encoder, output, tags);

            const int frameDurationMs = 20;
            int frameSize = opusSampleRate * frameDurationMs / 1000;
            int bytesPerSample = format.BitsPerSample / 8;
            int bytesPerFrame = frameSize * format.Channels * bytesPerSample;

            var buffer = new byte[bytesPerFrame];
            var samples = new short[frameSize * format.Channels];

            pcmSource.Position = 0;
            long totalBytes = pcmSource.Length;
            long processedBytes = 0;

            while (processedBytes < totalBytes)
            {
                int bytesToRead = (int)Math.Min(buffer.Length, totalBytes - processedBytes);
                int bytesRead = pcmSource.Read(buffer, 0, bytesToRead);
                if (bytesRead == 0)
                    break;

                processedBytes += bytesRead;
                int sampleCount = ConvertToSamples(buffer, bytesRead, samples, format);

                if (sampleCount > 0)
                {
                    if (format.SamplesPerSecond != opusSampleRate)
                    {
                        var resampledSamples = ResampleAudio(samples, sampleCount / format.Channels, 
                            format.SamplesPerSecond, opusSampleRate, format.Channels);
                        oggOut.WriteSamples(resampledSamples, 0, resampledSamples.Length / format.Channels);
                    }
                    else
                    {
                        oggOut.WriteSamples(samples, 0, sampleCount / format.Channels);
                    }
                }
            }

            oggOut.Finish();
        }

        private int ConvertToSamples(byte[] buffer, int byteCount, short[] samples, WaveFormat format)
        {
            int sampleCount = 0;
            int bytesPerSample = format.BitsPerSample / 8;
            int totalSamples = byteCount / bytesPerSample;

            for (int i = 0; i < totalSamples && sampleCount < samples.Length; i++)
            {
                int bufferIndex = i * bytesPerSample;

                switch (format.BitsPerSample)
                {
                    case 8:
                        samples[sampleCount] = (short)((buffer[bufferIndex] - 128) * 256);
                        break;

                    case 16:
                        samples[sampleCount] = BitConverter.ToInt16(buffer, bufferIndex);
                        break;

                    case 24:
                        int sample24 = buffer[bufferIndex] | 
                                      (buffer[bufferIndex + 1] << 8) | 
                                      (buffer[bufferIndex + 2] << 16);
                        if ((sample24 & 0x800000) != 0)
                            sample24 |= unchecked((int)0xFF000000);
                        samples[sampleCount] = (short)(sample24 >> 8);
                        break;

                    case 32:
                        int sample32 = BitConverter.ToInt32(buffer, bufferIndex);
                        samples[sampleCount] = (short)(sample32 >> 16);
                        break;

                    default:
                        throw new NotSupportedException($"Unsupported bit depth: {format.BitsPerSample}");
                }

                sampleCount++;
            }

            return sampleCount;
        }

        private short[] ResampleAudio(short[] input, int inputFrames, uint inputRate, 
            uint outputRate, int channels)
        {
            if (inputRate == outputRate)
                return input;

            double ratio = (double)inputRate / outputRate;
            int outputFrames = (int)(inputFrames / ratio);
            var output = new short[outputFrames * channels];

            for (int ch = 0; ch < channels; ch++)
            {
                for (int i = 0; i < outputFrames; i++)
                {
                    double srcIndex = i * ratio;
                    int srcIndexInt = (int)srcIndex;
                    double fraction = srcIndex - srcIndexInt;

                    if (srcIndexInt + 1 < inputFrames)
                    {
                        short sample1 = input[srcIndexInt * channels + ch];
                        short sample2 = input[(srcIndexInt + 1) * channels + ch];
                        output[i * channels + ch] = (short)(sample1 + fraction * (sample2 - sample1));
                    }
                    else if (srcIndexInt < inputFrames)
                    {
                        output[i * channels + ch] = input[srcIndexInt * channels + ch];
                    }
                }
            }

            return output;
        }
    }
}