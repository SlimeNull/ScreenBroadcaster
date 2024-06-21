using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
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
    }

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
    private bool _showMouseCursor = true;

    [ObservableProperty]
    private int _countForDroppingFrame = 20;

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
        AVCodecID.Av1,    // AV1 有毛病, 不能用
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
    private CursorLoader? _cursorLoader;
    private CodecContext? _videoEncoder;
    private SKSurface? _skSurface;
    private FrameData? _lastKeyFrame = null;
    private TcpListener? _tcpListener;
    private readonly List<TcpClientInfo> _clients = new();

    private Frame _bgraFrame = new Frame();
    private Frame _yuvFrame = new Frame();
    private Packet _packetRef = new Packet();
    private VideoFrameConverter _videoFrameConverter = new();

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

            _cursorLoader = new CursorLoader();

            // init SKSurface
            _skSurface = SKSurface.Create(new SKImageInfo(_screenCapture.ScreenWidth, _screenCapture.ScreenHeight, SKColorType.Bgra8888, SKAlphaType.Opaque), _screenCapture.DataPointer, _screenCapture.Stride);

            // init encoding
            _videoEncoder = new CodecContext(FFmpegUtilities.FindBestEncoder(CodecId, UseHardwareCodec))
            {
                Width = FrameWidth,
                Height = FrameHeight,
                Framerate = new AVRational(1, MaxFrameRate),
                TimeBase = new AVRational(1, MaxFrameRate),
                PixelFormat = PixelFormat,
                BitRate = BitRate,
                MaxBFrames = 0,
                GopSize = 10,
            };

            // correct the bitrate
            var codecName = _videoEncoder.Codec.Name;
            if (codecName == "libx264" ||
                codecName == "libx265" ||
                codecName == "libsvtav1")
            {
                // libx264 and libx256 bitrate is measured in kilobits
                _videoEncoder.BitRate /= 1000;

                // 最小是 1000 kbps
                _videoEncoder.BitRate = Math.Max(_videoEncoder.BitRate, 1000);
            }

            // network
            _tcpListener = new TcpListener(new IPEndPoint(ipAddress, Port));

            _cancellationTokenSource = new CancellationTokenSource();

            BroadcastTask = Task.WhenAll(
                NetworkLoop(),
                CaptureLoop(),
                BroadcastLoop()
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
    }


    public Task NetworkLoop()
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
                        newClientStream.WriteStruct(appInfo);
                        newClientStream.WriteStruct(screenInfo);
                    }

                    while (_lastKeyFrame is null)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            return;

                        Thread.Sleep(1);
                    }

                    _lastKeyFrame.Value.WriteToStream(newClientStream);

                    lock (_clients)
                    {
                        _clients.Add(new TcpClientInfo(newClient, new()));
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
                _ = Dispatcher.BeginInvoke(() =>
                {
                    MessageBox.Show(this, ex.Message, "Network Issue", MessageBoxButton.OK, MessageBoxImage.Error);
                    _ = Stop();
                });
            }
        });
    }

    public Task CaptureLoop()
    {
        return Task.Run(() =>
        {
            if (_screenCapture is null ||
                _cursorLoader is null ||
                _skSurface is null ||
                _videoEncoder is null ||
                _cancellationTokenSource is null)
                return;

            _cursorLoader.Initialize();

            var pts = 0;
            var cancellationToken = _cancellationTokenSource.Token;
            var mediaDictionary = new MediaDictionary();
            var lastFrameTime = DateTimeOffset.MinValue;
            var maxFrameRate = MaxFrameRate;
            var showMouseCursor = ShowMouseCursor;
            var cursor = default(CursorLoader.CursorData?);
            var cursorX = default(int);
            var cursorY = default(int);
            var cursorWidth = default(int);
            var cursorHeight = default(int);

            if (maxFrameRate == 0)
                maxFrameRate = 60;

            var codecName = _videoEncoder.Codec.Name;
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
                mediaDictionary["preset"] = "12";
            }
            else if (codecName == "h264_nvenc")
            {
                mediaDictionary["preset"] = "fast";
                mediaDictionary["tune"] = "ull";
            }


            try
            {
                _videoEncoder.Open(null, mediaDictionary);

                while (!cancellationToken.IsCancellationRequested)
                {
                    while ((DateTimeOffset.Now - lastFrameTime) < TimeSpan.FromSeconds(1.0 / maxFrameRate))
                    {
                        // wait
                    }

                    CURSORINFO cursorInfo = default;
                    unsafe
                    {
                        cursorInfo.cbSize = (uint)sizeof(CURSORINFO);
                    }

                    if (showMouseCursor &&
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

                    if (cursor is not null)
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

                    var nowTime = DateTimeOffset.Now;
                    var timestamp = nowTime.ToUnixTimeMilliseconds();
                    lastFrameTime = nowTime;

                    _bgraFrame.Width = _screenCapture.ScreenWidth;
                    _bgraFrame.Height = _screenCapture.ScreenHeight;
                    _bgraFrame.Format = (int)AVPixelFormat.Bgra;
                    _bgraFrame.Data[0] = _screenCapture.DataPointer;
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

                        if ((packet.Flags & ffmpeg.AV_PKT_FLAG_KEY) != 0)
                        {
                            isKeyFrame = true;
                        }
                    }

                    if (isKeyFrame)
                    {
                        _lastKeyFrame = new FrameData(timestamp, true, framePacketBytes);
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
                    MessageBox.Show(this, ex.Message, "Decoder Issue", MessageBoxButton.OK, MessageBoxImage.Error);
                    _ = Stop();
                });
            }
        });
    }

    public Task BroadcastLoop()
    {
        return Task.Run(() =>
        {
            if (_cancellationTokenSource is null)
                return;
            var cancellationToken = _cancellationTokenSource.Token;

            List<TcpClientInfo> clientsToRemove = new();

            while (!cancellationToken.IsCancellationRequested)
            {
                lock (_clients)
                {
                    clientsToRemove.Clear();
                    foreach (var client in _clients)
                    {
                        while (client.Frames.Count > CountForDroppingFrame &&
                              (ThrowsKeyFrame || client.Frames.Count(v => v.IsKeyFrame) > 2))
                        {
                            client.Frames.TryDequeue(out _);
                        }

                        if (client.Frames.TryDequeue(out var frameData))
                        {
                            try
                            {
                                var stream = client.TcpClient.GetStream();
                                frameData.WriteToStream(stream);
                            }
                            catch
                            {
                                clientsToRemove.Add(client);
                            }
                        }
                    }

                    foreach (var client in clientsToRemove)
                    {
                        _clients.Remove(client);
                    }
                }
            }
        });
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _ = Stop();
    }

    private void Hyperlink_Click(object sender, RoutedEventArgs e)
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
