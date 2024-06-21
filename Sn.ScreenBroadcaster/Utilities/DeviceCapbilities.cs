using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Raw;
using SharpDX.DXGI;

namespace Sn.ScreenBroadcaster.Utilities
{
    public record struct DeviceCapabilities(bool IsIntelGpuAvailable, bool IsAmdGpuAvailable, bool IsNvidiaGpuAvailable)
    {
        public static DeviceCapabilities Get()
        {
            bool isIntelGpuAvailable = false;
            bool isAmdGpuAvailable = false;
            bool isNvidiaGpuAvailable = false;

            using var factory = new Factory1();

            var adapterCount = factory.GetAdapterCount();
            for (int i = 0; i < adapterCount; i++)
            {
                using var adapter = factory.GetAdapter(i);

                if (adapter.Description.Description.Contains("Intel"))
                {
                    isIntelGpuAvailable = true;
                }
                else if (adapter.Description.Description.Contains("AMD"))
                {
                    isAmdGpuAvailable = true;
                }
                else if (adapter.Description.Description.Contains("NVIDIA"))
                {
                    isNvidiaGpuAvailable = true;
                }
            }

            return new DeviceCapabilities(isIntelGpuAvailable, isAmdGpuAvailable, isNvidiaGpuAvailable);
        }
    }
}
