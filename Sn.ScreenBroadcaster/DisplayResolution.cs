namespace Sn.ScreenBroadcaster;

public record struct DisplayResolution(int Width, int Height)
{
    public override string ToString()
    {
        return $"{Width}x{Height}";
    }
}