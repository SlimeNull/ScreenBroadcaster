using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Sdcb.FFmpeg.Codecs;
using Sn.ScreenBroadcaster.Utilities;

namespace Sn.ScreenBroadcaster.Data
{
    public record struct FrameData(long Timestamp, bool IsKeyFrame, List<byte[]> Packets)
    {
#if NET6_0_OR_GREATER
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
#else
        public void WriteToStream(Stream stream)
        {
            var timestamp = Timestamp;
            var isKeyFrame = IsKeyFrame;
            var packetCount = (ushort)Packets.Count;

            using BinaryWriter writer = new BinaryWriter(stream, Encoding.Default, true);

            writer.Write(timestamp);
            writer.Write(isKeyFrame);
            writer.Write(packetCount);

            foreach (var packet in Packets)
            {
                var packetSize = packet.Length;

                Console.WriteLine();
                writer.Write(packetSize);
                writer.Write(packet);
            }
        }

        public static FrameData ReadFromStream(Stream stream)
        {
            var timestamp = default(long);
            var isKeyFrame = default(byte);
            var packetCount = default(ushort);

            using BinaryReader reader = new BinaryReader(stream, Encoding.Default, true);

            timestamp = reader.ReadInt64();
            isKeyFrame = reader.ReadByte();
            packetCount = reader.ReadUInt16();

            var packets = new List<byte[]>();
            for (int i = 0; i < packetCount; i++)
            {
                var packetSize = default(int);

                packetSize = reader.ReadInt32();

                byte[] packetBody = reader.ReadBytes(packetSize);

                packets.Add(packetBody);
            }

            return new FrameData(timestamp, isKeyFrame != 0, packets);
        }
#endif
    }
}
