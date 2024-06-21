using System.Collections.Concurrent;
using System.Net.Sockets;

namespace Sn.ScreenBroadcaster.Data;

public record struct TcpClientInfo(TcpClient TcpClient, ConcurrentQueue<FrameData> Frames);
