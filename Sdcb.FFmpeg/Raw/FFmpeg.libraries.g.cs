using System;
using System.Runtime.InteropServices;

#pragma warning disable 169
#pragma warning disable CS0649
#pragma warning disable CS0108
namespace Sdcb.FFmpeg.Raw
{
    using System.Collections.Generic;
    
    public unsafe static partial class FFmpeg
    {
        public static Dictionary<string, int> LibraryVersionMap =  new ()
        {
            ["avcodec"] = -1,
            ["avdevice"] = -1,
            ["avfilter"] = -1,
            ["avformat"] = -1,
            ["avutil"] = -1,
            ["postproc"] = -1,
            ["swresample"] = -1,
            ["swscale"] = -1,
        };
    }
}
