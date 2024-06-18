using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LibCommon
{
    public record struct FrameData(long Timestamp, bool IsKeyFrame, List<byte[]> Packets)
    {
        public void WriteToStream(Stream stream)
        {
            var timestamp = Timestamp;
            var packetCount = (ushort)Packets.Count;

            stream.Write(MemoryMarshal.CreateSpan(ref Unsafe.As<long, byte>(ref timestamp), 8));
            stream.WriteByte(IsKeyFrame ? (byte)0x01 : (byte)0x00);
            stream.Write(MemoryMarshal.CreateSpan(ref Unsafe.As<ushort, byte>(ref packetCount), 2));

            foreach (var packet in Packets)
            {
                var packetSize = packet.Length;

                stream.Write(MemoryMarshal.CreateSpan(ref Unsafe.As<int, byte>(ref packetSize), 4));
                stream.Write(packet);
            }
        }
    }
}
