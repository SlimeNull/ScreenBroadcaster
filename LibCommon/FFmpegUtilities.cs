using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Raw;

namespace LibCommon
{
    public static class FFmpegUtilities
    {
        public static Codec FindBestEncoder(AVCodecID avCodecID)
        {
            foreach (var hardwareEncoder in Codec.FindEncoders(avCodecID).Where(codec => codec.Capabilities.HasFlag(AV_CODEC_CAP.Hardware) && codec.LongName.Contains("NVIDIA")))
            {
                return hardwareEncoder;
            }

            return Codec.FindEncoderById(avCodecID);
        }

        public static Codec FindBestDecoder(AVCodecID avCodecID)
        {
            foreach (var hardwareDecoder in Codec.FindDecoders(avCodecID).Where(codec => codec.Capabilities.HasFlag(AV_CODEC_CAP.Hardware) && codec.LongName.Contains("NVIDIA")))
            {
                return hardwareDecoder;
            }

            return Codec.FindDecoderById(avCodecID);
        }
    }
}
