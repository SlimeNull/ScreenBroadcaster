using System.Collections.Concurrent;
using System.Net.Sockets;
using LibCommon;

namespace Sn.ScreenBroadcaster;

public record struct TcpClientInfo(TcpClient TcpClient, ConcurrentQueue<FrameData> Frames);
