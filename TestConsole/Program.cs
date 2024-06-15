using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
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
ConcurrentQueue<byte[]> packetQueue = new();

// init encoding
using CodecContext videoEncoder = new CodecContext(Codec.CommonEncoders.Libx264)
{
    Width = capture.Width,
    Height = capture.Height,
    Framerate = new AVRational(1, 30),
    TimeBase = new AVRational(1, 30),
    PixelFormat = AVPixelFormat.Yuv420p,
    BitRate = 8000000
};

videoEncoder.Open(videoEncoder.Codec);

using Frame bgraFrame = new Frame();
using Packet packetRef = new Packet();
using VideoFrameConverter videoFrameConverter = new();


var networkTask = Task.Run(async () =>
{
    listener.Start();

    while (true)
    {
        var newClient = await listener.AcceptTcpClientAsync();

        lock (clients)
        {
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

        using Frame yuvFrame = videoEncoder.CreateFrame();

        yuvFrame.EnsureBuffer();
        yuvFrame.MakeWritable();

        videoFrameConverter.ConvertFrame(bgraFrame, yuvFrame);
        yuvFrame.Pts = pts;

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

            packetQueue.Enqueue(packetBytes);
        }
    }
});

var broadcastTask = Task.Run(() =>
{
    List<TcpClient> clientsToRemove = new();

    while (true)
    {
        lock (clients)
        {
            if (clients.Count == 0)
                continue;

            if (packetQueue.TryDequeue(out var packetBytes))
            {
                foreach (var client in clients)
                {
                    try
                    {
                        var clientStream = client.GetStream();
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


            foreach (var client in clientsToRemove)
            {
                clients.Remove(client);
            }
        }
    }
});

await Task.WhenAll(networkTask, captureTask, broadcastTask);
