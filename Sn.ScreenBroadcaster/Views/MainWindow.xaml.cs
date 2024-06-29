using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibScreenCapture;
using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Raw;
using Sdcb.FFmpeg.Swscales;
using Sdcb.FFmpeg.Toolboxs.Extensions;
using Sdcb.FFmpeg.Utils;
using SkiaSharp;
using Sn.ScreenBroadcaster.Data;
using Sn.ScreenBroadcaster.Data.Packets;
using Sn.ScreenBroadcaster.Utilities;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Sn.ScreenBroadcaster.Views;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
[ObservableObject]
public partial class MainWindow : Window
{
    public MainWindow()
    {
        DataContext = this;
        InitializeComponent();

        LoadScreens();
        Screen = AvailableScreens.FirstOrDefault();

        _totalScreenWidth = ScreenInfo.GetTotalScreenWidth(AvailableScreens);
        _totalScreenHeight = ScreenInfo.GetTotalScreenHeight(AvailableScreens);
    }

    // readonly

    private int _primaryScreenWidth = ScreenInfo.GetPrimaryScreenWidth();
    private int _primaryScreenHeight = ScreenInfo.GetPrimaryScreenHeight();
    private int _totalScreenWidth;
    private int _totalScreenHeight;

    // atom status

    private int _capturedFrameCount;




    // observable properties

    [ObservableProperty]
    private Socket[] _connectedClients = Array.Empty<Socket>();

    [ObservableProperty]
    private int _frameRate;

    [ObservableProperty]
    private TcpClient? _clientCanControl;

    [ObservableProperty]
    private string _controllerClientUserName = string.Empty;

    [ObservableProperty]
    private string _address = "0.0.0.0";

    [ObservableProperty]
    private int _port = 7651;

    [ObservableProperty]
    private CaptureMethod _captureMethod = CaptureMethod.DesktopDuplication;

    [ObservableProperty]
    private ScreenInfo _screen;

    [ObservableProperty]
    private ConfigMode _encodingConfigMode = ConfigMode.Simple;

    // simple encoding settings

    [ObservableProperty]
    private DisplayResolution _frameSize;

    [ObservableProperty]
    private BitRateMode _bitRateMode;

    // advanced encoding settings

    [ObservableProperty]
    private int _frameWidth = 1920;

    [ObservableProperty]
    private int _frameHeight = 1080;

    [ObservableProperty]
    private int _maxFrameRate = 30;

    [ObservableProperty]
    private int _bitRate = 8_000_000;

    [ObservableProperty]
    private AVCodecID _codecId = AVCodecID.H264;

    [ObservableProperty]
    private AVPixelFormat _pixelFormat = AVPixelFormat.Yuv420p;

    [ObservableProperty]
    private bool _useHardwareCodec = false;

    [ObservableProperty]
    private bool _captureMouseCursor = true;


    // network settings

    [ObservableProperty]
    private int _countForDroppingFrame = 20;

    [ObservableProperty]
    private bool _broadcastAddress = true;

    [ObservableProperty]
    private bool _throwsKeyFrame = false;

    public string AppVersion
    {
        get
        {
            string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
            var splitIndex = version.IndexOf('+');
            if (splitIndex != -1)
            {
                version = version.Substring(0, splitIndex);
            }

            return version;
        }
    }

    public ObservableCollection<BroadcasterServerInfo> AvailableServers { get; } = new();

    public ObservableCollection<ScreenInfo> AvailableScreens { get; } = new();

    public ObservableCollection<ConfigMode> AvailableConfigModes { get; } = new()
    {
        ConfigMode.Simple,
        ConfigMode.Advanced
    };

    public ObservableCollection<AVCodecID> AvailableCodecList { get; } = new()
    {
        AVCodecID.H264,
        AVCodecID.Hevc,     // HEVC 会有延迟
        //AVCodecID.Av1,    // AV1 有毛病, 不能用, 什么 Divide by zero exception. 搞不明白
    };

    public ObservableCollection<DisplayResolution> AvailableFrameSizes { get; } = new()
    {
        // pass
    };

    public ObservableCollection<BitRateMode> AvailableBitRateModes { get; } = new()
    {
        BitRateMode.Normal,
        BitRateMode.Large,
        BitRateMode.Small,
    };

    public ObservableCollection<AVPixelFormat> AvailablePixelFormatList { get; } = new()
    {
        AVPixelFormat.Yuv420p,
        AVPixelFormat.Yuv422p,
        AVPixelFormat.Yuv444p,
    };

    public ObservableCollection<CaptureMethod> AvailableCaptureMethodList { get; } = new()
    {
        CaptureMethod.DesktopDuplication,
        CaptureMethod.BitBlt,
    };



    private IScreenCapture? _screenCapture;
    private nint _screenFrameBuffer;

    private CursorLoader? _cursorLoader;
    private CodecContext? _videoEncoder;
    private SKSurface? _skSurface;
    private TcpListener? _tcpListener;
    private readonly List<TcpClientInfo> _clients = new();

    private Frame _bgraFrame = new Frame();
    private Frame _yuvFrame = new Frame();
    private Packet _packetRef = new Packet();
    private VideoFrameConverter _videoFrameConverter = new();

    private TcpClient? _notifyClientCanControl;
    private TcpClient? _notifyClientCanNotControl;
    private TcpClient? _notifyRejectClientControl;

    private CancellationTokenSource? _cancellationTokenSource;

    [ObservableProperty]
    private Task? _broadcastTask;

    private void LoadScreens()
    {
        AvailableScreens.Clear();

        foreach (var screen in ScreenInfo.GetScreens())
        {
            AvailableScreens.Add(screen);
        }
    }

    private int GetBitRate(int width, int height, BitRateMode mode)
    {
        var areaSize = (double)(width * height);
        var rate = areaSize / (2560 * 1440);

        var baseBitRate = mode switch
        {
            BitRateMode.Large => 8_000_000,
            BitRateMode.Normal => 4_000_000,
            BitRateMode.Small => 2_000_000,

            _ => 4_000_000
        };

        return (int)(baseBitRate * rate);
    }

    partial void OnScreenChanged(ScreenInfo value)
    {
        var screenWidth = value.Width;
        var screenHeight = value.Height;

        FrameWidth = screenWidth;
        FrameHeight = screenHeight;

        BitRate = GetBitRate(FrameWidth, FrameHeight, BitRateMode);

        AvailableFrameSizes.Clear();
        AvailableFrameSizes.Add(new DisplayResolution((int)(screenWidth), (int)(screenHeight)));
        AvailableFrameSizes.Add(new DisplayResolution((int)(screenWidth * 0.8), (int)(screenHeight * 0.8)));
        AvailableFrameSizes.Add(new DisplayResolution((int)(screenWidth * 0.75), (int)(screenHeight * 0.75)));
        AvailableFrameSizes.Add(new DisplayResolution((int)(screenWidth * 0.5), (int)(screenHeight * 0.5)));
        AvailableFrameSizes.Add(new DisplayResolution((int)(screenWidth * 0.4), (int)(screenHeight * 0.4)));
        AvailableFrameSizes.Add(new DisplayResolution((int)(screenWidth * 0.25), (int)(screenHeight * 0.25)));

        FrameSize = default;
        FrameSize = AvailableFrameSizes[0];
    }

    partial void OnFrameSizeChanged(DisplayResolution value)
    {
        FrameWidth = value.Width;
        FrameHeight = value.Height;

        BitRate = GetBitRate(FrameWidth, FrameHeight, BitRateMode);
    }

    partial void OnBitRateModeChanged(BitRateMode value)
    {
        BitRate = GetBitRate(FrameWidth, FrameHeight, BitRateMode);
    }

    [RelayCommand]
    private void Connect()
    {
        if (!IPAddress.TryParse(Address, out var ipAddress))
        {
            MessageBox.Show("Invalid IP Address", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }


        this.Hide();

        var clientWindow = new ClientWindow(this);
        clientWindow.Owner = this;
        clientWindow.Start();
        clientWindow.ShowDialog();

        this.Show();
    }

    [RelayCommand]
    private void Start()
    {
        if (BroadcastTask is { })
        {
            return;
        }

        if (!IPAddress.TryParse(Address, out var ipAddress))
        {
            MessageBox.Show("Invalid IP Address", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var displayIndex = AvailableScreens.IndexOf(Screen);
        var needDrawing = CaptureMouseCursor;

        if (displayIndex == -1)
        {
            MessageBox.Show("Invalid Screen", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        try
        {
            // capture
            _screenCapture = CaptureMethod switch
            {
                CaptureMethod.DesktopDuplication => new DirectScreenCapture(displayIndex),
                CaptureMethod.BitBlt => new GdiScreenCapture(displayIndex),
                _ => throw new Exception("This would never happened."),
            };


            if (_screenFrameBuffer != IntPtr.Zero)
            {
                unsafe
                {
#if NET6_0_OR_GREATER
                    NativeMemory.Free((void*)_screenFrameBuffer);
#else
                    Marshal.FreeHGlobal(_screenFrameBuffer);
#endif
                }

                _screenFrameBuffer = IntPtr.Zero;
            }

            if (needDrawing)
            {
                unsafe
                {
#if NET6_0_OR_GREATER
                    // init frame buffer
                    _screenFrameBuffer = (nint)NativeMemory.Alloc((nuint)(_screenCapture.Stride * _screenCapture.ScreenHeight));
#else
                    _screenFrameBuffer = Marshal.AllocHGlobal(_screenCapture.Stride * _screenCapture.ScreenHeight);
#endif
                }

                // init SKSurface
                _skSurface = SKSurface.Create(new SKImageInfo(_screenCapture.ScreenWidth, _screenCapture.ScreenHeight, SKColorType.Bgra8888, SKAlphaType.Opaque), _screenFrameBuffer, _screenCapture.Stride);
            }

            _cursorLoader = new CursorLoader();

            var codec = FFmpegUtilities.FindBestEncoder(CodecId, UseHardwareCodec);

            // correct the bitrate
            var bitRate = BitRate;
            var codecName = codec.Name;
            if (codecName == "libx264" ||
                codecName == "libx265" ||
                codecName == "libsvtav1")
            {
                // libx264 and libx256 bitrate is measured in kilobits
                bitRate /= 1000;

                // 最小是 1000 kbps
                bitRate = Math.Max(bitRate, 1000);
            }

            // init encoding
            _videoEncoder = new CodecContext(codec)
            {
                Width = FrameWidth,
                Height = FrameHeight,
                Framerate = new AVRational(1, MaxFrameRate),
                TimeBase = new AVRational(1, MaxFrameRate),
                PixelFormat = PixelFormat,
                BitRate = bitRate,
                MaxBFrames = 0,
                GopSize = 10,
            };

            // network
            _tcpListener = new TcpListener(new IPEndPoint(ipAddress, Port));

            _cancellationTokenSource = new CancellationTokenSource();

            ClientCanControl = null;
            BroadcastTask = Task.WhenAll(
                BroadcastLoop(),
                NetworkLoop(),
                CaptureLoop(),
                StatusLoop()
            );
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to start broadcasting. {ex.Message}", "Initialization Issue", MessageBoxButton.OK, MessageBoxImage.Error);
            _ = Stop();
        }
    }

    [RelayCommand]
    private async Task Stop()
    {
        _cancellationTokenSource?.Cancel();

        try
        {
            if (BroadcastTask is not null)
                await BroadcastTask;
        }
        catch { }
        finally
        {
            BroadcastTask = null;
        }

        try
        {
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            _tcpListener?.Stop();
            _tcpListener = null;
            _screenCapture?.Dispose();
            _screenCapture = null;
            _cursorLoader?.Dispose();
            _cursorLoader = null;
            _skSurface?.Dispose();
            _skSurface = null;

            _videoEncoder?.Dispose();
            _videoEncoder = null;
        }
        catch { }

        if (_screenFrameBuffer != IntPtr.Zero)
        {
            unsafe
            {
#if NET6_0_OR_GREATER
                NativeMemory.Free((void*)_screenFrameBuffer);
#else
                Marshal.FreeHGlobal(_screenFrameBuffer);
#endif
            }

            _screenFrameBuffer = IntPtr.Zero;
        }

        foreach (var client in _clients)
        {
            try
            {
                client.TcpClient.Close();
                client.TcpClient.Dispose();
            }
            catch { }
        }

        _clients.Clear();
        ConnectedClients = Array.Empty<Socket>();
        ClientCanControl = null;
    }

    private Task BroadcastLoop()
    {
        if (!BroadcastAddress)
            return Task.CompletedTask;

        var broadcastMessage = default(byte[]);

        unsafe
        {
            var messageValue = NetworkBroadcastData.Create();
            var messageSpan = new Span<byte>((byte*)&messageValue, sizeof(NetworkBroadcastData));

            broadcastMessage = messageSpan.ToArray();
        }

        return Task.Run(async () =>
        {
            if (_cancellationTokenSource is null)
                return;


            var cancellationToken = _cancellationTokenSource.Token;
            var udpClient = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
            var remoteEndPoint = new IPEndPoint(IPAddress.Broadcast, 7650);

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(3000, cancellationToken);
                    await udpClient.SendAsync(broadcastMessage, broadcastMessage.Length, remoteEndPoint);
                }
            }
            catch (OperationCanceledException)
            {
                // pass
            }
        });
    }

    private Task NetworkLoop()
    {
        return Task.Run(async () =>
        {
            if (_tcpListener is null ||
                _cancellationTokenSource is null ||
                _videoEncoder is null)
                return;

            var appInfo = new BroadcasterAppInfo()
            {
                Version = Assembly.GetExecutingAssembly().GetName().Version?.Major ?? 0
            };

            var screenInfo = new BroadcasterScreenInfo()
            {
                Width = FrameWidth,
                Height = FrameHeight,
                CodecID = (int)_videoEncoder.CodecId,
                PixelFormat = (int)_videoEncoder.PixelFormat,
            };

            var cancellationToken = _cancellationTokenSource.Token;

            try
            {
                _tcpListener.Start();

                while (!cancellationToken.IsCancellationRequested)
                {
#if NET6_0_OR_GREATER
                    var newClient = await _tcpListener.AcceptTcpClientAsync(cancellationToken);
#else
                    TcpClient newClient;
                    using (cancellationToken.Register(() =>
                    {
                        _tcpListener.Stop();
                    }))
                    {

                        newClient = await _tcpListener.AcceptTcpClientAsync();
                    }
#endif
                    var newClientStream = newClient.GetStream();

                    unsafe
                    {
                        newClientStream.WriteValue(appInfo);
                        newClientStream.WriteValue(screenInfo);
                    }

                    lock (_clients)
                    {
                        var newClientInfo = new TcpClientInfo(newClient, new());

                        _clients.Add(newClientInfo);

                        _ = ClientSendingLoop(newClientInfo);
                        _ = ClientReceivingLoop(newClientInfo);
                    }

                    Dispatcher.Invoke(() =>
                    {
                        ConnectedClients = _clients
                            .Select(client => client.TcpClient.Client)
                            .ToArray();
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // pass
            }
            catch (ObjectDisposedException)
            {
                // pass
            }
            catch (Exception ex)
            {
                _ = Dispatcher.BeginInvoke(() =>
                {
                    MessageBox.Show(this, ex.Message, "Network Issue", MessageBoxButton.OK, MessageBoxImage.Error);
                    _ = Stop();
                });
            }
        });
    }

    private Task CaptureLoop()
    {
        return Task.Run(() =>
        {
            if (_screenCapture is null ||
                _videoEncoder is null ||
                _cancellationTokenSource is null)
                return;

            if (_cursorLoader is not null)
            {
                _cursorLoader.Initialize();
            }

            var pts = 0;
            var cancellationToken = _cancellationTokenSource.Token;
            var lastFrameTime = DateTimeOffset.MinValue;
            var maxFrameRate = MaxFrameRate;
            var frameByteCount = _screenCapture.Stride * _screenCapture.ScreenHeight;
            var captureMouseCursor = CaptureMouseCursor;
            var cursor = default(CursorLoader.CursorData?);
            var cursorX = default(int);
            var cursorY = default(int);
            var cursorWidth = default(int);
            var cursorHeight = default(int);

            if (maxFrameRate == 0)
                maxFrameRate = 60;

            var codecName = _videoEncoder.Codec.Name;

            using (var mediaDictionary = new MediaDictionary())
            {
                if (codecName == "libx264" ||
                    codecName == "libx265")
                {
                    //mediaDictionary["crf"] = "30";
                    mediaDictionary["tune"] = "zerolatency";
                    mediaDictionary["preset"] = "veryfast";
                }
                else if (codecName == "libsvtav1")
                {
                    //mediaDictionary["tune"] = "zerolatency";
                    mediaDictionary["preset"] = "9";
                }
                else if (codecName == "h264_nvenc")
                {
                    mediaDictionary["preset"] = "fast";
                    mediaDictionary["tune"] = "ull";
                }

                try
                {
                    _videoEncoder.Open(null, mediaDictionary);
                }
                catch (Exception ex)
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        MessageBox.Show(this, $"Failed to open encoder. {ex.Message}", "Encoder Issue", MessageBoxButton.OK, MessageBoxImage.Error);
                        _ = Stop();
                    });

                    return;
                }
            }


            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    while (maxFrameRate != 0 && (DateTimeOffset.Now - lastFrameTime) < TimeSpan.FromSeconds(1.0 / maxFrameRate))
                    {
                        // wait
                    }

                    var nowTime = DateTimeOffset.Now;
                    var timestamp = nowTime.ToUnixTimeMilliseconds();
                    lastFrameTime = nowTime;

                    CURSORINFO cursorInfo = default;
                    unsafe
                    {
                        cursorInfo.cbSize = (uint)sizeof(CURSORINFO);
                    }

                    if (captureMouseCursor &&
                        _cursorLoader is not null &&
                        PInvoke.GetCursorInfo(ref cursorInfo) &&
                        cursorInfo.flags == CURSORINFO_FLAGS.CURSOR_SHOWING)
                    {
                        cursor = _cursorLoader.GetCursor(cursorInfo.hCursor);

                        if (cursor is not null)
                        {
                            cursorX = cursorInfo.ptScreenPos.X - _screenCapture.ScreenX - ((int)cursor.Value.HotspotX * _screenCapture.DpiX / 96);
                            cursorY = cursorInfo.ptScreenPos.Y - _screenCapture.ScreenY - ((int)cursor.Value.HotspotY * _screenCapture.DpiY / 96);

                            cursorWidth = cursor.Value.Width * _screenCapture.DpiX / 96;
                            cursorHeight = cursor.Value.Height * _screenCapture.DpiY / 96;
                        }
                    }

                    _screenCapture.Capture(TimeSpan.FromSeconds(0.1));
                    Interlocked.Add(ref _capturedFrameCount, 1);

                    var frameDataPtr = _screenCapture.DataPointer;

                    if (_screenFrameBuffer != 0)
                    {
                        unsafe
                        {
#if NET7_0_OR_GREATER
                            NativeMemory.Copy((void*)frameDataPtr, (void*)_screenFrameBuffer, (nuint)frameByteCount);
#else
                            Buffer.MemoryCopy((void*)frameDataPtr, (void*)_screenFrameBuffer, frameByteCount, frameByteCount);
#endif
                        }

                        frameDataPtr = _screenFrameBuffer;
                    }

                    if (cursor is not null && _skSurface != null)
                    {
                        if (cursor.Value.DirectBitmap is not null)
                        {
                            _skSurface.Canvas.DrawBitmap(cursor.Value.DirectBitmap, new SKRect(cursorX, cursorY, cursorX + cursorWidth, cursorY + cursorHeight));
                        }

                        if (cursor.Value.InvertBitmap is not null)
                        {
                            SKPaint paint = new SKPaint();
                            paint.BlendMode = SKBlendMode.Difference;

                            _skSurface.Canvas.DrawBitmap(cursor.Value.InvertBitmap, new SKRect(cursorX, cursorY, cursorX + cursorWidth, cursorY + cursorHeight), paint);
                        }
                    }


                    _bgraFrame.Width = _screenCapture.ScreenWidth;
                    _bgraFrame.Height = _screenCapture.ScreenHeight;
                    _bgraFrame.Format = (int)AVPixelFormat.Bgra;
                    _bgraFrame.Data[0] = frameDataPtr;
                    _bgraFrame.Linesize[0] = _screenCapture.Stride;
                    _bgraFrame.Pts = pts++;

                    _yuvFrame.Width = FrameWidth;
                    _yuvFrame.Height = FrameHeight;
                    _yuvFrame.Format = (int)_videoEncoder.PixelFormat;

                    _yuvFrame.EnsureBuffer();
                    _yuvFrame.MakeWritable();

                    _videoFrameConverter.ConvertFrame(_bgraFrame, _yuvFrame);
                    _yuvFrame.Pts = pts;

                    bool isKeyFrame = false;
                    List<byte[]> framePacketBytes = new();

                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    bool isFirstFrame = true;
                    foreach (var packet in _videoEncoder.EncodeFrame(_yuvFrame, _packetRef))
                    {
                        byte[] packetBytes = new byte[packet.Data.Length];

                        unsafe
                        {
                            fixed (byte* packetBytesPtr = packetBytes)
                            {
#if NET6_0_OR_GREATER
                                NativeMemory.Copy((void*)packet.Data.Pointer, packetBytesPtr, (nuint)packetBytes.Length);
#else
                                Buffer.MemoryCopy((void*)packet.Data.Pointer, packetBytesPtr, (nuint)packetBytes.Length, (nuint)packetBytes.Length);
#endif
                            }
                        }

                        framePacketBytes.Add(packetBytes);

                        if (isFirstFrame && (packet.Flags & FFmpeg.AV_PKT_FLAG_KEY) != 0)
                        {
                            isKeyFrame = true;
                        }

                        isFirstFrame = false;
                    }

                    if (framePacketBytes.Count != 0)
                    {
                        lock (_clients)
                        {
                            foreach (var client in _clients)
                            {
                                client.Frames.Enqueue(new FrameData(timestamp, isKeyFrame, framePacketBytes));
                            }
                        }
                    }

                }
            }
            catch (OperationCanceledException)
            {
                // pass
            }
            catch (ObjectDisposedException)
            {
                // pass
            }
            catch (Exception ex)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    MessageBox.Show(this, ex.Message, "Encoder Issue", MessageBoxButton.OK, MessageBoxImage.Error);
                    _ = Stop();
                });
            }
        });
    }

    private Task ClientSendingLoop(TcpClientInfo clientInfo)
    {
        return Task.Run(() =>
        {
            if (_cancellationTokenSource is null)
                return;

            var cancellationToken = _cancellationTokenSource.Token;
            var clientStream = clientInfo.TcpClient.GetStream();
            while (!cancellationToken.IsCancellationRequested)
            {
                while (clientInfo.Frames.Count > CountForDroppingFrame &&
                      (ThrowsKeyFrame || clientInfo.Frames.Count(v => v.IsKeyFrame) > 2))
                {
                    clientInfo.Frames.TryDequeue(out _);
                }

                if (clientInfo.Frames.TryDequeue(out var frameData))
                {
                    try
                    {
                        clientStream.WriteValue(ServerToClientPacketKind.Frame);
                        frameData.WriteToStream(clientStream);
                    }
                    catch
                    {
                        lock (_clients)
                        {
                            _clients.Remove(clientInfo);
                        }

                        Dispatcher.Invoke(() =>
                        {
                            if (ClientCanControl == clientInfo.TcpClient)
                            {
                                ClientCanControl = null;
                            }

                            ConnectedClients = _clients
                                .Select(client => client.TcpClient.Client)
                                .ToArray();
                        });
                    }
                }

                if (_notifyClientCanControl == clientInfo.TcpClient)
                {
                    _notifyClientCanControl = null;
                    clientStream.WriteValue(ServerToClientPacketKind.NotifyCanControl);
                    clientStream.WriteValue(new GrantControlPacket()
                    {
                        IsAdministrator = PermissionUtilities.IsAdministrator(),
                    });
                }
                else if (_notifyClientCanNotControl == clientInfo.TcpClient)
                {
                    _notifyClientCanNotControl = null;
                    clientStream.WriteValue(ServerToClientPacketKind.NotifyCanNotControl);
                }
                else if (_notifyRejectClientControl == clientInfo.TcpClient)
                {
                    _notifyRejectClientControl = null;
                    clientStream.WriteValue(ServerToClientPacketKind.NotifyRejectControl);
                }
            }
        });
    }

    private Task ClientReceivingLoop(TcpClientInfo clientInfo)
    {
        return Task.Run(() =>
        {
            if (_cancellationTokenSource is null)
                return;

            var cancellationToken = _cancellationTokenSource.Token;
            var clientStream = clientInfo.TcpClient.GetStream();
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var packetKind = clientStream.ReadValue<ClientToServerPacketKind>();

                    if (packetKind == ClientToServerPacketKind.Control)
                    {
                        var control = clientStream.ReadValue<ControlPacket>();

                        if (ClientCanControl != clientInfo.TcpClient)
                            continue;

                        Windows.Win32.UI.Input.KeyboardAndMouse.INPUT input = default;
                        input.type = (Windows.Win32.UI.Input.KeyboardAndMouse.INPUT_TYPE)control.Kind;

                        switch (control.Kind)
                        {
                            case ControlPacket.ControlKind.Mouse:
                            {
                                if ((control.Input.MouseInput.dwFlags | Windows.Win32.UI.Input.KeyboardAndMouse.MOUSE_EVENT_FLAGS.MOUSEEVENTF_ABSOLUTE) != 0)
                                {
                                    control.Input.MouseInput.dx = control.Input.MouseInput.dx * Screen.Width / _totalScreenWidth;
                                    control.Input.MouseInput.dy = control.Input.MouseInput.dy * Screen.Height / _totalScreenHeight;

                                    // 参考屏幕偏移量
                                    control.Input.MouseInput.dx += Screen.X * 65535 / _totalScreenWidth;
                                    control.Input.MouseInput.dy += Screen.Y * 65535 / _totalScreenHeight;
                                }

                                input.Anonymous.mi = control.Input.MouseInput;

                                break;
                            }
                            case ControlPacket.ControlKind.Keyboard:
                            {
                                input.Anonymous.ki = control.Input.KeyboardInput;
                                break;
                            }
                            case ControlPacket.ControlKind.Hardware:
                            {
                                input.Anonymous.hi = control.Input.HardwareInput;
                                break;
                            }
                        }

                        unsafe
                        {
                            PInvoke.SendInput(new System.Span<Windows.Win32.UI.Input.KeyboardAndMouse.INPUT>(&input, 1), sizeof(Windows.Win32.UI.Input.KeyboardAndMouse.INPUT));
                        }
                    }
                    else if (packetKind == ClientToServerPacketKind.RequestControl)
                    {
                        unsafe
                        {
                            var request = clientStream.ReadValue<RequestControlPacket>();
                            var clientUserName = new string(request.UserName);

                            _ = Dispatcher.InvokeAsync(() =>
                            {
                                WindowState = WindowState.Normal;

                                Show();
                                Activate();

                                var windowInteropHelper = new WindowInteropHelper(this);
                                var result = PInvoke.MessageBoxTimeout(
                                    windowInteropHelper.Handle,
                                    string.Format((string)FindResource("StringFormat.ClientRequestControl"), clientUserName),
                                    (string)FindResource("String.GrantControlPermission"),
                                    MESSAGEBOX_STYLE.MB_YESNO | MESSAGEBOX_STYLE.MB_ICONQUESTION,
                                    0,
                                    10000);

                                if (result == MESSAGEBOX_RESULT.IDYES)
                                {
                                    if (ClientCanControl != null &&
                                        ClientCanControl != clientInfo.TcpClient)
                                    {
                                        _notifyClientCanNotControl = ClientCanControl;
                                    }

                                    _notifyClientCanControl = clientInfo.TcpClient;
                                    _ = Dispatcher.InvokeAsync(() =>
                                    {
                                        ClientCanControl = clientInfo.TcpClient;
                                        ControllerClientUserName = clientUserName;
                                    });
                                }
                                else
                                {
                                    _notifyRejectClientControl = clientInfo.TcpClient;
                                }
                            });
                        }
                    }
                    else if (packetKind == ClientToServerPacketKind.RelinquishControl)
                    {
                        if (ClientCanControl == clientInfo.TcpClient)
                        {
                            _ = Dispatcher.InvokeAsync(() =>
                            {
                                ClientCanControl = null;
                                ControllerClientUserName = string.Empty;
                            });
                        }
                    }
                }
                catch (IOException)
                {
                    // pass
                    break;
                }
                catch (SocketException)
                {
                    // pass
                    break;
                }
            }
        });
    }

    private Task QueryServersLoop()
    {
        return Task.WhenAll(
            Task.Run(RemoveTimeoutServers),
            Task.Run(QueryNewServersLoop));

        async Task RemoveTimeoutServers()
        {
            var timeout = TimeSpan.FromSeconds(10);
            var timeoutServers = new List<BroadcasterServerInfo>();

            while (true)
            {
                await Task.Delay(1000);

                var timeNow = DateTimeOffset.Now;
                timeoutServers.Clear();

                foreach (var server in AvailableServers)
                {
                    if ((timeNow - server.LastTime) > timeout)
                    {
                        timeoutServers.Add(server);
                    }
                }

                foreach (var server in timeoutServers)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        AvailableServers.Remove(server);
                    });
                }
            }
        }

        async Task QueryNewServersLoop()
        {
            var udpClient = new UdpClient(new IPEndPoint(IPAddress.Any, 7650));

            while (true)
            {
                var received = await udpClient.ReceiveAsync();
                var message = default(NetworkBroadcastData);

                unsafe
                {
                    if (received.Buffer.Length != sizeof(NetworkBroadcastData))
                    {
                        continue;
                    }

                    fixed (byte* dataPtr = received.Buffer)
                    {
                        message = *(NetworkBroadcastData*)dataPtr;
                    }
                }

                if (!message.IsValid())
                {
                    continue;
                }

                if (AvailableServers.FirstOrDefault(server => server.RemoteEndPoint.Equals(received.RemoteEndPoint)) is { } server)
                {
                    server.LastTime = DateTimeOffset.Now;
                }
                else
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        AvailableServers.Add(new BroadcasterServerInfo(received.RemoteEndPoint));
                    });
                }
            }
        }
    }

    private async Task StatusLoop()
    {
        if (_cancellationTokenSource is null)
            return;

        var cancellationToken = _cancellationTokenSource.Token;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1000, cancellationToken);

                FrameRate = _capturedFrameCount;
                Interlocked.Exchange(ref _capturedFrameCount, 0);
            }
        }
        catch (OperationCanceledException)
        {
            // pass
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _ = QueryServersLoop();
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _ = Stop();
    }

    private void StopControl_Click(object sender, RoutedEventArgs e)
    {
        _notifyClientCanNotControl = ClientCanControl;
        ClientCanControl = null;
    }

    private void BrowserHyperlink_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Hyperlink hyperlink)
                return;

            Process.Start(new ProcessStartInfo()
            {
                FileName = hyperlink.NavigateUri.ToString(),
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Failed to open hyperlink", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
