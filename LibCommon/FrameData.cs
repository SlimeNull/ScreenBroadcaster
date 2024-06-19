using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sdcb.FFmpeg.Codecs;

namespace LibCommon
{
    public record struct FrameData(long Timestamp, bool IsKeyFrame, List<byte[]> Packets)
    {
        public void WriteToStream(Stream stream)
        {
            var timestamp = Timestamp;
            var isKeyFrame = IsKeyFrame;
            var packetCount = (ushort)Packets.Count;

            stream.Write(MemoryMarshal.CreateSpan(ref Unsafe.As<long, byte>(ref timestamp), 8));
            stream.Write(MemoryMarshal.CreateSpan(ref Unsafe.As<bool, byte>(ref isKeyFrame), 1));
            stream.Write(MemoryMarshal.CreateSpan(ref Unsafe.As<ushort, byte>(ref packetCount), 2));

            foreach (var packet in Packets)
            {
                var packetSize = packet.Length;
                
                Console.WriteLine();
                stream.Write(MemoryMarshal.CreateSpan(ref Unsafe.As<int, byte>(ref packetSize), 4));
                stream.Write(packet);
            }
        }

        public static FrameData ReadFromStream(Stream stream)
        {
            var timestamp = default(long);
            var isKeyFrame = default(byte);
            var packetCount = default(ushort);

            stream.ReadBlock(MemoryMarshal.CreateSpan(ref Unsafe.As<long, byte>(ref timestamp), 8));
            stream.ReadBlock(MemoryMarshal.CreateSpan(ref isKeyFrame, 1));
            stream.ReadBlock(MemoryMarshal.CreateSpan(ref Unsafe.As<ushort, byte>(ref packetCount), 2));


            var packets = new List<byte[]>();
            for (int i = 0; i < packetCount; i++)
            {
                var packetSize = default(int);

                stream.ReadBlock(MemoryMarshal.CreateSpan(ref Unsafe.As<int, byte>(ref packetSize), 4));

                byte[] packetBody = new byte[packetSize];
                stream.ReadBlock(packetBody);

                packets.Add(packetBody);
            }

            return new FrameData(timestamp, isKeyFrame != 0, packets);
        }
    }
}
