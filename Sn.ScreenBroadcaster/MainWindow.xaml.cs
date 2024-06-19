using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibCommon;
using LibScreenCapture;
using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Raw;
using Sdcb.FFmpeg.Swscales;
using Sdcb.FFmpeg.Toolboxs.Extensions;
using Sdcb.FFmpeg.Utils;
using Windows.Win32;

namespace Sn.ScreenBroadcaster;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
[ObservableObject]
public partial class MainWindow : Window
{
    public MainWindow()
    {
        var desktopWindow = PInvoke.GetDesktopWindow();
        var desktopDC = PInvoke.GetDC(desktopWindow);
        _frameWidth = PInvoke.GetDeviceCaps(desktopDC, Windows.Win32.Graphics.Gdi.GET_DEVICE_CAPS_INDEX.DESKTOPHORZRES);
        _frameHeight = PInvoke.GetDeviceCaps(desktopDC, Windows.Win32.Graphics.Gdi.GET_DEVICE_CAPS_INDEX.DESKTOPVERTRES);

        PInvoke.ReleaseDC(desktopWindow, desktopDC);

        DataContext = this;
        InitializeComponent();

    }

    [ObservableProperty]
    private string _address = "0.0.0.0";

    [ObservableProperty]
    private int _port = 7651;
    
    [ObservableProperty]
    private CaptureMethod _captureMethod = CaptureMethod.DesktopDuplication;
    
    [ObservableProperty]
    private int _displayIndex = 0;

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
    private int _countForDroppingFrame = 20;
    
    [ObservableProperty]
    private bool _throwsKeyFrame = false;


    public ObservableCollection<AVCodecID> AvailableCodecList { get; } = new() 
    {
        AVCodecID.H264,
        AVCodecID.Hevc,     // HEVC 会有延迟
        //AVCodecID.Av1,    // AV1 有毛病, 不能用
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
    private CodecContext? _videoEncoder;
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
        if(BroadcastTask is { })
        {
            return;
        }

        if (!IPAddress.TryParse(Address, out var ipAddress))
        {
            MessageBox.Show("Invalid IP Address", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // capture
        _screenCapture = CaptureMethod switch
        {
            CaptureMethod.DesktopDuplication => new DirectScreenCapture(DisplayIndex),
            CaptureMethod.BitBlt => new GdiScreenCapture(),
            _ => throw new Exception("This would never happend."),
        };

        // init encoding
        _videoEncoder = new CodecContext(Codec.FindEncoderById(CodecId))
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

        // network
        _tcpListener = new TcpListener(new IPEndPoint(ipAddress, Port));

        _cancellationTokenSource = new CancellationTokenSource();

        BroadcastTask = Task.WhenAll(
            NetworkLoop(),
            CaptureLoop(),
            BroadcastLoop()
        );
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

        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
        _tcpListener?.Stop();
        _tcpListener = null;
        _videoEncoder?.Dispose();
        _videoEncoder = null;
        _screenCapture?.Dispose();
        _screenCapture = null;

        foreach (var client in _clients)
        {
            try
            {
                client.TcpClient.Close();
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
                _cancellationTokenSource is null||
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
                    var newClient = await _tcpListener.AcceptTcpClientAsync();
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
                _videoEncoder is null ||
                _cancellationTokenSource is null)
                return;

            var pts = 0;
            var cancellationToken = _cancellationTokenSource.Token;
            var mediaDictionary = new MediaDictionary();
            var lastFrameTime = DateTimeOffset.MinValue;
            var maxFrameRate = MaxFrameRate;

            if (maxFrameRate == 0)
                maxFrameRate = 60;

            var codecName = _videoEncoder.Codec.Name;
            if (codecName == "libx264")
            {
                //mediaDictionary["crf"] = "30";
                mediaDictionary["tune"] = "zerolatency";
                mediaDictionary["preset"] = "veryfast";
            }
            else if (codecName == "libx265")
            {
                //mediaDictionary["crf"] = "30";
                mediaDictionary["tune"] = "zerolatency";
                mediaDictionary["preset"] = "veryfast";
            }
            else if (codecName == "libaom-av1")
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

                while (!cancellationToken.IsCancellationRequested)
                {
                    while ((DateTimeOffset.Now - lastFrameTime) < TimeSpan.FromSeconds(1.0 / maxFrameRate))
                    {
                        // wait
                    }

                    _screenCapture.Capture(TimeSpan.FromSeconds(0.1));

                    var nowTime = DateTimeOffset.Now;
                    var timestamp = nowTime.ToUnixTimeMilliseconds();
                    lastFrameTime = nowTime;

                    _bgraFrame.Width = _screenCapture.Width;
                    _bgraFrame.Height = _screenCapture.Height;
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
            if(_cancellationTokenSource is null)
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
}


public record struct TcpClientInfo(TcpClient TcpClient, ConcurrentQueue<FrameData> Frames);