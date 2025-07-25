using System.ComponentModel.Composition;

namespace GameRes.Formats.Regrips
{
    [Export(typeof(AudioFormat))]
    public class WrgAudio : AudioFormat
    {
        public override string         Tag { get { return "WRG"; } }
        public override string Description { get { return "Regrips encrypted WAVE file"; } }
        public override uint     Signature { get { return 0xB9B9B6AD; } }
        public override bool      CanWrite { get { return false; } }

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var input = new XoredStream (file.AsStream, 0xFF);
            return Wav.TryOpen (new BinaryStream (input, file.Name));
        }
    }

    [Export(typeof(AudioFormat))]
    public class MrgAudio : AudioFormat
    {
        public override string         Tag { get { return "MRG"; } }
        public override string Description { get { return "Regrips encrypted MP3 file"; } }
        public override uint     Signature { get { return 0; } }
        public override bool      CanWrite { get { return false; } }

        static readonly ResourceInstance<AudioFormat> Mp3Format = new ResourceInstance<AudioFormat> ("MP3");

        public override SoundInput TryOpen (IBinaryStream file)
        {
            var header = file.ReadHeader (2);
            if (header[0] != 0)
                return null;
            file.Position = 0;
            var input = new XoredStream (file.AsStream, 0xFF);
            return Mp3Format.Value.TryOpen (new BinaryStream (input, file.Name));
        }
    }
}
