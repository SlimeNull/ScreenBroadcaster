using Sdcb.FFmpeg.Raw;
using System;
using System.Runtime.InteropServices;
using System.Text;
using static Sdcb.FFmpeg.Raw.FFmpeg;

namespace Sdcb.FFmpeg.Utils;

public static class NameUtils
{
    /// <summary>
    /// <see cref="av_get_pix_fmt_name(AVPixelFormat)"/>
    /// </summary>
    public static string GetPixelFormatName(AVPixelFormat pixelFormat) => av_get_pix_fmt_name(pixelFormat);

    public static string GetSampleFormatName(AVSampleFormat sampleFormat) => av_get_sample_fmt_name(sampleFormat);

    /// <summary>
    /// <see cref="av_get_pix_fmt(string)"/>
    /// </summary>
    public static AVPixelFormat ToPixelFormat(string name) => av_get_pix_fmt(name);
}

public static class AVChannelLayoutExtensions
{
    /// <summary>
    /// <see cref="av_channel_layout_describe"/>
    /// </summary>
    public unsafe static string Describe(this AVChannelLayout chLayout)
    {
        byte[] buffer = new byte[64];
        fixed (byte* ptr = buffer)
        {
            av_channel_layout_describe(&chLayout, ptr, (ulong)buffer.Length);
            return PtrExtensions.PtrToStringUTF8((IntPtr)ptr)!;
        }
    }
}