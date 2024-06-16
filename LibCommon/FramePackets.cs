namespace LibCommon
{
    public record struct FramePackets(bool IsKeyFrame, List<byte[]> PacketsBytes);
}
