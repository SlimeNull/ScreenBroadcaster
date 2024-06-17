namespace LibCommon
{
    public record struct FrameData(long Timestamp, bool IsKeyFrame, List<byte[]> Packets);
}
