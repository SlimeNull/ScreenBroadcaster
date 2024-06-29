using System.Net;

namespace Sn.ScreenBroadcaster.Data;

public class BroadcasterServerInfo
{
    public IPEndPoint RemoteEndPoint { get; }
    public DateTimeOffset LastTime { get; set; }

    public BroadcasterServerInfo(IPEndPoint remoteEndPoint) : this(remoteEndPoint, DateTimeOffset.Now)
    {

    }

    public BroadcasterServerInfo(IPEndPoint remoteEndPoint, DateTimeOffset lastTime)
    {
        RemoteEndPoint = remoteEndPoint;
        LastTime = lastTime;
    }

    public override string ToString()
    {
        return $"{RemoteEndPoint}";
    }
}