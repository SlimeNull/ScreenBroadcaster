using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using LibCommon;
using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Raw;
using Sdcb.FFmpeg.Swscales;
using Sdcb.FFmpeg.Utils;

namespace Sn.ScreenBroadcasterClient
{
    public partial class MainForm : Form
    {
        TcpClient _client = new();
        Frame frame = new Frame();

        ConcurrentQueue<FrameData> framePacketBytes = new();
        FrameData? lastKeyFrame = default;

        CodecContext _codecContext = new(FFmpegUtilities.FindBestDecoder(AVCodecID.H264))
        {
            Width = 2560,
            Height = 1440,
            PixelFormat = AVPixelFormat.Yuv420p,
        };

        public MainForm()
        {
            InitializeComponent();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            Task.Run(async () =>
            {
                _client.Connect(new IPEndPoint(IPAddress.Loopback, 7777));

                var networkStream = _client.GetStream();
                var frameTimestampBytes = new byte[8];
                var frameIsKeyFrameBytes = new byte[4];
                var framePacketCountBytes = new byte[4];
                var packetSizeBytes = new byte[4];

                while (true)
                {
                    await networkStream.ReadBlockAsync(frameTimestampBytes, 0, 8);
                    await networkStream.ReadBlockAsync(frameIsKeyFrameBytes, 0, 4);
                    await networkStream.ReadBlockAsync(framePacketCountBytes, 0, 4);
                    
                    var timestamp = BitConverter.ToInt64(frameTimestampBytes);
                    var framePacketCount = BitConverter.ToInt32(framePacketCountBytes);
                    var frameIsKeyFrame = BitConverter.ToInt32(frameIsKeyFrameBytes);

                    List<byte[]> framePacketBytes = new();
                    for (int i = 0; i < framePacketCount; i++)
                    {
                        await networkStream.ReadBlockAsync(packetSizeBytes, 0, 4);
                        var packetSize = BitConverter.ToInt32(packetSizeBytes);
                        var body = new byte[packetSize];

                        await networkStream.ReadBlockAsync(body, 0, packetSize);
                        framePacketBytes.Add(body);
                    }

                    var nowTimestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                    await Console.Out.WriteLineAsync($"Received frame. Latency from capture: {nowTimestamp - timestamp}ms");

                    var currentFramePackets = new FrameData(timestamp, frameIsKeyFrame != 0, framePacketBytes);
                    this.framePacketBytes.Enqueue(currentFramePackets);

                    if (currentFramePackets.IsKeyFrame)
                    {
                        lastKeyFrame = currentFramePackets;
                    }
                }
            });

            Task.Run(() =>
            {
                _codecContext.Open(_codecContext.Codec);

                var isDecoder = _codecContext.Codec.IsDecoder;
                var graphics = paintControl.CreateGraphics();
                var bufferedGraphics = BufferedGraphicsManager.Current.Allocate(graphics, paintControl.ClientRectangle);

                var bitmap = default(Bitmap);
                var videoFrameConverter = new VideoFrameConverter();

                var dropFrameCounter = 1;

                while (true)
                {
                    while (this.framePacketBytes.Count > 10 &&
                        this.framePacketBytes.Where(frame => frame.IsKeyFrame).Count() > 1)
                    {
                        if (!this.framePacketBytes.TryPeek(out var peeked))
                            continue;
                        if (peeked == lastKeyFrame)
                            break;

                        this.framePacketBytes.TryDequeue(out _);
                        Console.WriteLine($"Drop frame. {dropFrameCounter}");

                        dropFrameCounter++;
                    }

                    if (this.framePacketBytes.TryDequeue(out var frameData))
                    {
                        if (frameData == lastKeyFrame)
                        {
                            lastKeyFrame = null;
                        }

                        foreach (var packetBytes in frameData.Packets)
                        {
                            unsafe
                            {
                                fixed (byte* bodyPtr = packetBytes)
                                {
                                    AVPacket avPacket = default;
                                    ffmpeg.av_packet_from_data(&avPacket, bodyPtr, packetBytes.Length);

                                    using Packet packet = Packet.FromNative(&avPacket, false);

                                    _codecContext.SendPacket(packet);
                                }
                            }

                            CodecResult result;

                            do
                            {
                                result = _codecContext.ReceiveFrame(frame);

                                if (result == CodecResult.Success)
                                {
                                    using var convertedFrame = new Frame()
                                    {
                                        Width = frame.Width,
                                        Height = frame.Height,
                                        Format = (int)AVPixelFormat.Bgra,
                                    };

                                    convertedFrame.EnsureBuffer();
                                    convertedFrame.MakeWritable();
                                    videoFrameConverter.ConvertFrame(frame, convertedFrame);

                                    var nowTimestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                                    Console.WriteLine($"Frame Decoded. Latency from captured: {nowTimestamp - frameData.Timestamp}ms");


                                    if (bitmap == null ||
                                        bitmap.Width != frame.Width ||
                                        bitmap.Height != frame.Height)
                                    {
                                        if (bitmap is not null)
                                        {
                                            bitmap.Dispose();
                                        }

                                        bitmap = new Bitmap(frame.Width, frame.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
                                    }
                                    var bmpData = bitmap.LockBits(new Rectangle(default(Point), bitmap.Size), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);

                                    for (int y = 0; y < bitmap.Height; y++)
                                    {
                                        unsafe
                                        {
                                            NativeMemory.Copy((void*)(convertedFrame.Data[0] + convertedFrame.Linesize[0] * y), (void*)(bmpData.Scan0 + bmpData.Stride * y), (nuint)bmpData.Stride);
                                        }
                                    }

                                    bitmap.UnlockBits(bmpData);

                                    LayoutUtilities.Uniform(paintControl.Width, paintControl.Height, bitmap.Width, bitmap.Height, out var imgX, out var imgY, out var imgActualWidth, out var imgActualHeight);
                                    bufferedGraphics.Graphics.DrawImage(bitmap, imgX, imgY, imgActualWidth, imgActualHeight);
                                    bufferedGraphics.Render();

                                    nowTimestamp = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                                    Console.WriteLine($"Frame drawn. Latency from captured: {nowTimestamp - frameData.Timestamp}ms");
                                }
                            }
                            while (result == CodecResult.Success);
                        }
                    }
                }
            });
        }
    }
}
