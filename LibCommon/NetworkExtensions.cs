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

        public static void ReadBlock(this Stream stream, Span<byte> buffer)
        {
            var received = 0;

            while (received < buffer.Length)
            {
                received += stream.Read(buffer.Slice(received, buffer.Length - received));
            }
        }
    }
}
