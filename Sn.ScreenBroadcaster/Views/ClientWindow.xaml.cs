using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Common;
using Sdcb.FFmpeg.Raw;
using Sdcb.FFmpeg.Swscales;
using Sdcb.FFmpeg.Utils;
using Sn.ScreenBroadcaster.Data;
using Sn.ScreenBroadcaster.Utilities;

namespace Sn.ScreenBroadcaster.Views
{
    /// <summary>
    /// Interaction logic for ClientWindow.xaml
    /// </summary>
    [ObservableObject]
    public partial class ClientWindow : Window
    {
        private readonly MainWindow _owner;

        private bool _firstFrameDecoded;

        private TcpClient? _tcpClient;
        private CodecContext? _videoDecoder;
        private VideoFrameConverter? _videoFrameConverter;

        private readonly Frame _yuvFrame = new Frame();
        private readonly ConcurrentQueue<FrameData> _frames = new();

        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _clientTask;

        private WriteableBitmap? _frameBitmap;

        public ClientWindow(MainWindow owner)
        {
            _owner = owner;
            DataContext = this;
            InitializeComponent();
        }

        public void Start()
        {
            if (!IPAddress.TryParse(_owner.Address, out var ipAddress))
                throw new Exception("This would never happen");

            _tcpClient = new TcpClient();
            _videoFrameConverter = new();
            _cancellationTokenSource = new();

            _clientTask = Task.WhenAll(
                NetworkLoop(new IPEndPoint(ipAddress, _owner.Port)),
                DecodeLoop());
        }

        public async Task Stop()
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource = null;

            try
            {
                if (_clientTask is not null)
                {
                    await _clientTask;
                }
            }
            catch { }
            finally
            {
            }

            _tcpClient?.Dispose();
            _videoDecoder?.Dispose();
            _videoFrameConverter?.Dispose();

#if NET6_0_OR_GREATER
            _frames.Clear();
#else
            while (_frames.Count > 0)
            {
                _frames.TryDequeue(out _);
            }
#endif


            _tcpClient = null;
            _videoDecoder = null;
            _videoFrameConverter = null;
            _clientTask = null;
            _frameBitmap = null;
        }

        public async Task StopAndClose()
        {
            await Stop();
            Close();
        }

        private Task NetworkLoop(IPEndPoint remoteIPEndPoint)
        {
            return Task.Run(async () =>
            {
                if (_tcpClient is null ||
                    _cancellationTokenSource is null)
                {
                    return;
                }

                try
                {
                    await _tcpClient.ConnectAsync(remoteIPEndPoint.Address, remoteIPEndPoint.Port);

                    var clientStream = _tcpClient.GetStream();
                    var appInfo = default(BroadcasterAppInfo);
                    var screenInfo = default(BroadcasterScreenInfo);
                    var cancellationToken = _cancellationTokenSource.Token;

                    unsafe
                    {
                        appInfo = clientStream.ReadStruct<BroadcasterAppInfo>();
                        screenInfo = clientStream.ReadStruct<BroadcasterScreenInfo>();
                    }

                    if (appInfo.Version != (Assembly.GetExecutingAssembly().GetName().Version?.Major ?? 0))
                    {
                        _ = Dispatcher.BeginInvoke(() =>
                        {
                            MessageBox.Show(this, "Remote server version does not match current client version", "Version Issue", MessageBoxButton.OK, MessageBoxImage.Error);
                            _ = StopAndClose();
                        });
                    }

                    if (_videoDecoder is not null)
                    {
                        _videoDecoder.Dispose();
                        // throw new Exception("This would never happen");
                    }

                    _videoDecoder = new CodecContext(FFmpegUtilities.FindBestDecoder((Sdcb.FFmpeg.Raw.AVCodecID)screenInfo.CodecID, _owner.UseHardwareCodec))
                    {
                        Width = screenInfo.Width,
                        Height = screenInfo.Height,
                        PixelFormat = (Sdcb.FFmpeg.Raw.AVPixelFormat)screenInfo.PixelFormat
                    };

                    _videoDecoder.Open();

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        // block here
                        var frame = FrameData.ReadFromStream(clientStream);

                        lock (_frames)
                        {
                            _frames.Enqueue(frame);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // pass
                }
                catch (FFmpegException ex)
                {
                    _ = Dispatcher.BeginInvoke(() =>
                    {
                        MessageBox.Show(this, ex.Message, "Decoder Issue", MessageBoxButton.OK, MessageBoxImage.Error);
                        _ = StopAndClose();
                    });
                }
                catch (EndOfStreamException)
                {
                    _ = Dispatcher.BeginInvoke(() =>
                    {
                        MessageBox.Show(this, "Disconnected from remote server", "Network Issue", MessageBoxButton.OK, MessageBoxImage.Error);
                        _ = StopAndClose();
                    });
                }
                catch (Exception ex)
                {
                    _ = Dispatcher.BeginInvoke(() =>
                    {
                        MessageBox.Show(this, ex.Message, "Network Issue", MessageBoxButton.OK, MessageBoxImage.Error);
                        _ = StopAndClose();
                    });
                }
            });
        }

        private Task DecodeLoop()
        {
            return Task.Run(() =>
            {
                if (_videoFrameConverter is null ||
                    _cancellationTokenSource is null)
                    return;

                var cancellationToken = _cancellationTokenSource.Token;

                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        lock (_frames)
                        {
                            while (_frames.Count > _owner.CountForDroppingFrame &&
                                   (_owner.ThrowsKeyFrame || _frames.Count(v => v.IsKeyFrame) > 2))
                            {
                                _frames.TryDequeue(out _);
                            }

                            if (_videoDecoder is null)
                            {
                                continue;
                            }

                            if (!_frames.TryDequeue(out var frame))
                            {
                                continue;
                            }

                            if (!_firstFrameDecoded && !frame.IsKeyFrame)
                            {
                                continue;
                            }

                            _firstFrameDecoded = true;

                            bool isFirstPacket = true;
                            foreach (var packetBytes in frame.Packets)
                            {
                                unsafe
                                {
                                    fixed (byte* bodyPtr = packetBytes)
                                    {
                                        AVPacket avPacket = default;
                                        FFmpeg.av_packet_from_data(&avPacket, bodyPtr, packetBytes.Length);

                                        using Packet packet = Packet.FromNative(&avPacket, false);

                                        if (isFirstPacket && frame.IsKeyFrame)
                                        {
                                            packet.Flags = 1;
                                        }

                                        _videoDecoder.SendPacket(packet);
                                        isFirstPacket = false;
                                    }

                                    var codecResult = _videoDecoder.ReceiveFrame(_yuvFrame);
                                    if (codecResult == CodecResult.Success)
                                    {
                                        using var convertedFrame = new Frame()
                                        {
                                            Width = _yuvFrame.Width,
                                            Height = _yuvFrame.Height,
                                            Format = (int)AVPixelFormat.Bgra,
                                        };

                                        convertedFrame.EnsureBuffer();
                                        convertedFrame.MakeWritable();
                                        _videoFrameConverter.ConvertFrame(_yuvFrame, convertedFrame);

                                        Dispatcher.Invoke(() =>
                                        {
                                            if (_frameBitmap is null ||
                                                _frameBitmap.PixelWidth != convertedFrame.Width ||
                                                _frameBitmap.PixelHeight != convertedFrame.Height)
                                            {
                                                _frameBitmap = new WriteableBitmap(convertedFrame.Width, convertedFrame.Height, 96, 96, PixelFormats.Bgra32, null);
                                            }

                                            _frameBitmap.WritePixels(new Int32Rect(0, 0, _frameBitmap.PixelWidth, _frameBitmap.PixelHeight), convertedFrame.Data[0], convertedFrame.Linesize[0] * convertedFrame.Height, convertedFrame.Linesize[0]);
                                            //_frameBitmap.Lock();
                                            //NativeMemory.Copy((void*)convertedFrame.Data[0], (void*)_frameBitmap.BackBuffer, (nuint)(convertedFrame.Linesize[0] * convertedFrame.Height));
                                            //_frameBitmap.Unlock();

                                            frameImage.Source = _frameBitmap;
                                        });
                                    }
                                }
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // pass
                }
                catch (Exception ex)
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        MessageBox.Show(this, ex.Message, "Decoder Issue", MessageBoxButton.OK, MessageBoxImage.Error);
                        _ = StopAndClose();
                    });
                }
            });
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _ = Stop();
        }
    }
}
