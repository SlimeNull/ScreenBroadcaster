namespace LibCommon
{
    public static class NetworkExtensions
    {
        public static async Task ReadBlockAsync(this Stream stream, byte[] buffer, int offset, int count)
        {
            var received = 0;

            while (received < count)
            {
                received += await stream.ReadAsync(buffer, offset + received, count - received);
            }
        }
    }

    public struct BroadcastInfo
    {
        public int Width;
        public int Height;
        public int CodecID;
    }
}
