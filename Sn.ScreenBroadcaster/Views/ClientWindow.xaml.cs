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
        private readonly ConcurrentQueue<ControlPacketData> _controlPackets = new();

        private CancellationTokenSource? _cancellationTokenSource;
        private Task? _clientTask;

        private WriteableBitmap? _frameBitmap;

        private BroadcasterAppInfo _appInfo;
        private BroadcasterScreenInfo _screenInfo;
        private bool _requestControl;
        private bool _relinquishControl;



        [ObservableProperty]
        private bool _canControl;

        [ObservableProperty]
        private GrantControlInfo _controlInfo;




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

            try
            {
                _tcpClient.Connect(ipAddress, _owner.Port);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Failed to connect", MessageBoxButton.OK, MessageBoxImage.Error);
                _ = StopAndClose();
                return;
            }

            _clientTask = Task.WhenAll(
                NetworkReceivingLoop(),
                NetworkSendingLoop(),
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

        private Task NetworkReceivingLoop()
        {
            return Task.Run(() =>
            {
                if (_tcpClient is null ||
                    _cancellationTokenSource is null)
                {
                    return;
                }

                try
                {
                    var clientStream = _tcpClient.GetStream();
                    var cancellationToken = _cancellationTokenSource.Token;

                    unsafe
                    {
                        _appInfo = clientStream.ReadValue<BroadcasterAppInfo>();
                        _screenInfo = clientStream.ReadValue<BroadcasterScreenInfo>();
                    }

                    if (_appInfo.Version != (Assembly.GetExecutingAssembly().GetName().Version?.Major ?? 0))
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

                    _videoDecoder = new CodecContext(FFmpegUtilities.FindBestDecoder((Sdcb.FFmpeg.Raw.AVCodecID)_screenInfo.CodecID, _owner.UseHardwareCodec))
                    {
                        Width = _screenInfo.Width,
                        Height = _screenInfo.Height,
                        PixelFormat = (Sdcb.FFmpeg.Raw.AVPixelFormat)_screenInfo.PixelFormat
                    };

                    _videoDecoder.Open();

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var packetKind = clientStream.ReadValue<ServerToClientPacketKind>();

                        if (packetKind == ServerToClientPacketKind.Frame)
                        {
                            // block here
                            var frame = FrameData.ReadFromStream(clientStream);

                            lock (_frames)
                            {
                                _frames.Enqueue(frame);
                            }
                        }
                        else if (packetKind == ServerToClientPacketKind.NotifyCanControl)
                        {
                            var info = clientStream.ReadValue<GrantControlInfo>();

                            _ = Dispatcher.InvokeAsync(() =>
                            {
                                CanControl = true;
                                ControlInfo = info;
                            });
                        }
                        else if (packetKind == ServerToClientPacketKind.NotifyCanNotControl)
                        {
                            _ = Dispatcher.InvokeAsync(() =>
                            {
                                CanControl = false;
                            });
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

        private Task NetworkSendingLoop()
        {
            return Task.Run(() =>
            {
                if (_tcpClient is null ||
                    _cancellationTokenSource is null)
                {
                    return;
                }

                var clientStream = _tcpClient.GetStream();
                var cancellationToken = _cancellationTokenSource.Token;

                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        if (_controlPackets.TryDequeue(out var controlPacketData))
                        {
                            clientStream.WriteValue(ClientToServerPacketKind.Control);
                            clientStream.WriteValue(controlPacketData);
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

        private void FrameImage_MouseMove(object sender, MouseEventArgs e)
        {
            if (sender is not FrameworkElement element)
                return;
            if (!CanControl)
                return;

            var relativePosition = e.MouseDevice.GetPosition(element);
            var x = (relativePosition.X / element.ActualWidth) * 65535;
            var y = (relativePosition.Y / element.ActualHeight) * 65535;

            var control =
                new ControlPacketData()
                {
                    Kind = ControlPacketData.ControlKind.Mouse,
                };

            control.Input.MouseInput.dx = (int)x;
            control.Input.MouseInput.dy = (int)y;
            control.Input.MouseInput.dwFlags = Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_MOVE | Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_ABSOLUTE;

            _controlPackets.Enqueue(control);
        }

        private void FrameImage_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element)
                return;
            if (!CanControl)
                return;

            Mouse.Capture(element);

            var relativePosition = e.MouseDevice.GetPosition(element);
            var x = (relativePosition.X / element.ActualWidth) * 65535;
            var y = (relativePosition.Y / element.ActualHeight) * 65535;

            var control =
                new ControlPacketData()
                {
                    Kind = ControlPacketData.ControlKind.Mouse,
                };

            control.Input.MouseInput.dx = (int)x;
            control.Input.MouseInput.dy = (int)y;
            control.Input.MouseInput.dwFlags = Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_MOVE | Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_ABSOLUTE;

            if (e.ChangedButton == MouseButton.Left && e.MouseDevice.LeftButton == MouseButtonState.Pressed)
            {
                control.Input.MouseInput.dwFlags |= Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTDOWN;
            }
            else if (e.ChangedButton == MouseButton.Right && e.MouseDevice.RightButton == MouseButtonState.Pressed)
            {
                control.Input.MouseInput.dwFlags |= Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTDOWN;
            }
            else if (e.ChangedButton == MouseButton.Middle && e.MouseDevice.MiddleButton == MouseButtonState.Pressed)
            {
                control.Input.MouseInput.dwFlags |= Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_MIDDLEDOWN;
            }

            _controlPackets.Enqueue(control);
        }

        private void FrameImage_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element)
                return;
            if (!CanControl)
                return;

            if (element.IsMouseCaptured && 
                e.LeftButton == MouseButtonState.Released &&
                e.RightButton == MouseButtonState.Released &&
                e.MiddleButton == MouseButtonState.Released)
            {
                element.ReleaseMouseCapture();
            }

            var relativePosition = e.MouseDevice.GetPosition(element);
            var x = (relativePosition.X / element.ActualWidth) * 65535;
            var y = (relativePosition.Y / element.ActualHeight) * 65535;

            var control =
                new ControlPacketData()
                {
                    Kind = ControlPacketData.ControlKind.Mouse,
                };

            control.Input.MouseInput.dx = (int)x;
            control.Input.MouseInput.dy = (int)y;
            control.Input.MouseInput.dwFlags = Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_MOVE | Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_ABSOLUTE;

            if (e.ChangedButton == MouseButton.Left && e.MouseDevice.LeftButton == MouseButtonState.Released)
            {
                control.Input.MouseInput.dwFlags |= Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP;
            }
            else if (e.ChangedButton == MouseButton.Right && e.MouseDevice.RightButton == MouseButtonState.Released)
            {
                control.Input.MouseInput.dwFlags |= Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTUP;
            }
            else if (e.ChangedButton == MouseButton.Middle && e.MouseDevice.MiddleButton == MouseButtonState.Released)
            {
                control.Input.MouseInput.dwFlags |= Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_MIDDLEUP;
            }

            _controlPackets.Enqueue(control);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _ = Stop();
        }
    }
}
