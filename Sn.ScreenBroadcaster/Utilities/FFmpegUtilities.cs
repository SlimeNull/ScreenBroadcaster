using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Raw;

namespace Sn.ScreenBroadcaster.Utilities
{
    public static class FFmpegUtilities
    {
        public static Codec FindBestEncoder(DeviceCapabilities deviceCapabilities, AVCodecID avCodecID, bool useHardwareEncoder)
        {
            IEnumerable<Codec> targetEncoders = Codec.FindEncoders(avCodecID);

            if (avCodecID == AVCodecID.Av1)
            {
                targetEncoders = targetEncoders.Where(v => v.Name is not "libaom-av1" and not "librav1e");
            }

            if (!useHardwareEncoder)
            {
                return targetEncoders.First();
            }

            if (deviceCapabilities.IsAmdGpuAvailable)
            {
                foreach (var encoder in targetEncoders.Where(codec => codec.Name.EndsWith("amf", StringComparison.OrdinalIgnoreCase)))
                {
                    return encoder;
                }
            }
            else if (deviceCapabilities.IsNvidiaGpuAvailable)
            {
                foreach (var encoder in targetEncoders.Where(codec => codec.Name.EndsWith("nvenc", StringComparison.OrdinalIgnoreCase)))
                {
                    return encoder;
                }
            }
            else if (deviceCapabilities.IsIntelGpuAvailable)
            {
                foreach (var encoder in targetEncoders.Where(codec => codec.Name.EndsWith("qsv", StringComparison.OrdinalIgnoreCase)))
                {
                    return encoder;
                }
            }

            return targetEncoders.First();
        }

        public static Codec FindBestDecoder(DeviceCapabilities deviceCapabilities, AVCodecID avCodecID, bool useHardwareDecoder)
        {
            IEnumerable<Codec> targetDecoders = Codec.FindDecoders(avCodecID);

            if (avCodecID == AVCodecID.Av1)
            {
                targetDecoders = targetDecoders.Where(v => v.Name is not "libaom-av1" and not "librav1e");
            }

            if (!useHardwareDecoder)
            {
                return Codec.FindDecoderById(avCodecID);
            }

            if (deviceCapabilities.IsAmdGpuAvailable)
            {
                foreach (var decoder in targetDecoders.Where(codec => codec.Name.EndsWith("amf", StringComparison.OrdinalIgnoreCase)))
                {
                    return decoder;
                }
            }
            else if (deviceCapabilities.IsNvidiaGpuAvailable)
            {
                foreach (var decoder in targetDecoders.Where(codec => codec.Name.EndsWith("cuvid", StringComparison.OrdinalIgnoreCase)))
                {
                    return decoder;
                }
            }
            else if (deviceCapabilities.IsIntelGpuAvailable)
            {
                foreach (var decoder in targetDecoders.Where(codec => codec.Name.EndsWith("qsv", StringComparison.OrdinalIgnoreCase)))
                {
                    return decoder;
                }
            }

            return targetDecoders.First();
        }

        public static Codec FindBestEncoder(AVCodecID avCodecID, bool useHardwareEncoder)
            => FindBestEncoder(DeviceCapabilities.Get(), avCodecID, useHardwareEncoder);

        public static Codec FindBestDecoder(AVCodecID avCodecID, bool useHardwareDecoder)
            => FindBestDecoder(DeviceCapabilities.Get(), avCodecID, useHardwareDecoder);
    }
}
