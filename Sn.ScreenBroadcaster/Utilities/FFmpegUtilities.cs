using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Raw;

namespace Sn.ScreenBroadcaster.Utilities
{
    public static class FFmpegUtilities
    {
        public static Codec FindBestEncoder(DeviceCapabilities deviceCapabilities, AVCodecID avCodecID, bool useHardwareEncoder)
        {
            if (!useHardwareEncoder)
            {
                return Codec.FindEncoderById(avCodecID);
            }

            if (deviceCapabilities.IsAmdGpuAvailable)
            {
                foreach (var encoder in Codec.FindEncoders(avCodecID).Where(codec => codec.Name.EndsWith("amf", StringComparison.OrdinalIgnoreCase)))
                {
                    return encoder;
                }
            }
            else if (deviceCapabilities.IsNvidiaGpuAvailable)
            {
                foreach (var encoder in Codec.FindEncoders(avCodecID).Where(codec => codec.Name.EndsWith("nvenc", StringComparison.OrdinalIgnoreCase)))
                {
                    return encoder;
                }
            }

            return Codec.FindEncoderById(avCodecID);
        }

        public static Codec FindBestDecoder(DeviceCapabilities deviceCapabilities, AVCodecID avCodecID, bool useHardwareDecoder)
        {
            if (!useHardwareDecoder)
            {
                return Codec.FindDecoderById(avCodecID);
            }

            if (deviceCapabilities.IsAmdGpuAvailable)
            {
                foreach (var decoder in Codec.FindDecoders(avCodecID).Where(codec => codec.Name.EndsWith("amf", StringComparison.OrdinalIgnoreCase)))
                {
                    return decoder;
                }
            }
            else if (deviceCapabilities.IsNvidiaGpuAvailable)
            {
                foreach (var decoder in Codec.FindDecoders(avCodecID).Where(codec => codec.Name.EndsWith("nvenc", StringComparison.OrdinalIgnoreCase)))
                {
                    return decoder;
                }
            }

            return Codec.FindDecoderById(avCodecID);
        }

        public static Codec FindBestEncoder(AVCodecID avCodecID, bool useHardwareEncoder)
            => FindBestEncoder(DeviceCapabilities.Get(), avCodecID, useHardwareEncoder);

        public static Codec FindBestDecoder(AVCodecID avCodecID, bool useHardwareDecoder)
            => FindBestDecoder(DeviceCapabilities.Get(), avCodecID, useHardwareDecoder);
    }
}
