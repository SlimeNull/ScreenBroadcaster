using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using LibCommon;
using LibScreenCapture;
using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Raw;
using Sdcb.FFmpeg.Swscales;
using Sdcb.FFmpeg.Toolboxs.Extensions;
using Sdcb.FFmpeg.Utils;
using SkiaSharp;
using Spectre.Console;

// capture
IScreenCapture capture = new DirectScreenCapture(0);

// network
TcpListener listener = new TcpListener(new IPEndPoint(IPAddress.Loopback, 7777));
List<TcpClient> clients = new List<TcpClient>();

// encoding
ConcurrentQueue<FramePackets> framePacketQueue = new();
FramePackets? lastKeyFramePackets = null;

// init encoding
using CodecContext videoEncoder = new CodecContext(FFmpegUtilities.FindBestEncoder(AVCodecID.H264))
{
    Width = capture.Width,
    Height = capture.Height,
    Framerate = new AVRational(1, 30),
    TimeBase = new AVRational(1, 30),
    PixelFormat = AVPixelFormat.Yuv420p,
    GopSize = 20
    //BitRate = 80000
};

videoEncoder.Open(null);

using Frame bgraFrame = new Frame();
using Frame yuvFrame = new Frame();
using Packet packetRef = new Packet();
using VideoFrameConverter videoFrameConverter = new();

int captureCounter = 0;
int broadcastCounter = 0;


var networkTask = Task.Run(async () =>
{
    listener.Start();

    while (true)
    {
        var newClient = await listener.AcceptTcpClientAsync();

        lock (clients)
        {
            while (lastKeyFramePackets is null)
            {
                Thread.Sleep(1);
            }

            var clientStream = newClient.GetStream();
            var framePacketCount = lastKeyFramePackets.Value.PacketsBytes.Count;
            var frameIsKeyFrame = 1;

            var framePacketCountBytes = BitConverter.GetBytes(framePacketCount);
            var frameIsKeyFrameBytes = BitConverter.GetBytes(frameIsKeyFrame);

            clientStream.Write(framePacketCountBytes);
            clientStream.Write(frameIsKeyFrameBytes);

            foreach (var packetBytes in lastKeyFramePackets.Value.PacketsBytes)
            {
                var packetSizeBytes = BitConverter.GetBytes(packetBytes.Length);

                clientStream.Write(packetSizeBytes);
                clientStream.Write(packetBytes);
            }

            clients.Add(newClient);
        }
    }
});

var captureTask = Task.Run(() =>
{
    int pts = 0;

    while (true)
    {
        capture.Capture(TimeSpan.FromSeconds(0.1));

        bgraFrame.Width = capture.Width;
        bgraFrame.Height = capture.Height;
        bgraFrame.Format = (int)AVPixelFormat.Bgra;
        bgraFrame.Data[0] = capture.DataPointer;
        bgraFrame.Linesize[0] = capture.Stride;
        bgraFrame.Pts = pts++;

        yuvFrame.Width = videoEncoder.Width;
        yuvFrame.Height = videoEncoder.Height;
        yuvFrame.Format = (int)videoEncoder.PixelFormat;

        yuvFrame.EnsureBuffer();
        yuvFrame.MakeWritable();

        videoFrameConverter.ConvertFrame(bgraFrame, yuvFrame);
        yuvFrame.Pts = pts;

        bool isKeyFrame = false;
        List<byte[]> framePacketBytes = new();

        foreach (var packet in videoEncoder.EncodeFrame(yuvFrame, packetRef))
        {
            byte[] packetBytes = new byte[packet.Data.Length];

            unsafe
            {
                fixed (byte* packetBytesPtr = packetBytes)
                {
                    NativeMemory.Copy((void*)packet.Data.Pointer, packetBytesPtr, (nuint)packetBytes.Length);
                }
            }

            framePacketBytes.Add(packetBytes);

            if ((packet.Flags & ffmpeg.AV_PKT_FLAG_KEY) != 0)
            {
                isKeyFrame = true;
            }
        }

        if (isKeyFrame)
        {
            lastKeyFramePackets = new FramePackets(true, framePacketBytes);
        }

        if (framePacketBytes.Count != 0)
        {
            framePacketQueue.Enqueue(new FramePackets(isKeyFrame, framePacketBytes));
        }

        captureCounter++;
    }
});

var broadcastTask = Task.Run(() =>
{
    List<TcpClient> clientsToRemove = new();

    while (true)
    {
        while (framePacketQueue.Count > 5)
        {
            framePacketQueue.TryDequeue(out _);
            Console.WriteLine("Drop frame");
        }

        lock (clients)
        {
            while (framePacketQueue.TryDequeue(out var framePacketBytes))
            {
                var framePacketCount = framePacketBytes.PacketsBytes.Count;
                var frameIsKeyFrame = framePacketBytes.IsKeyFrame ? 1 : 0;

                var framePacketCountBytes = BitConverter.GetBytes(framePacketCount);
                var frameIsKeyFrameBytes = BitConverter.GetBytes(frameIsKeyFrame);

                foreach (var client in clients)
                {
                    var clientStream = client.GetStream();

                    clientStream.Write(framePacketCountBytes);
                    clientStream.Write(frameIsKeyFrameBytes);

                    foreach (var packetBytes in framePacketBytes.PacketsBytes)
                    {
                        try
                        {
                            var packetSizeBytes = BitConverter.GetBytes(packetBytes.Length);

                            clientStream.Write(packetSizeBytes);
                            clientStream.Write(packetBytes);

                            Console.WriteLine("Frame sent to client");
                        }
                        catch
                        {
                            clientsToRemove.Add(client);
                        }
                    }
                }
            }


            foreach (var client in clientsToRemove)
            {
                clients.Remove(client);
            }
        }

        broadcastCounter++;
    }
});

var counterTask = Task.Run(async () =>
{
    while (true)
    {
        await Task.Delay(1000);
        Console.WriteLine($"Capture: {captureCounter}/s; Broadcast: {broadcastCounter}/s");
        captureCounter = 0;
        broadcastCounter = 0;
    }
});

//await Task.WhenAll(networkTask, captureTask, broadcastTask, counterTask);
await Task.Delay(-1);
