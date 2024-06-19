using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using LibCommon;
using LibScreenCapture;
using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Raw;
using Sdcb.FFmpeg.Swscales;
using Sdcb.FFmpeg.Toolboxs.Extensions;
using Sdcb.FFmpeg.Utils;
using SharpDX.Direct3D11;

namespace TestWPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        IScreenCapture _screenCapture;
        CodecContext _videoEncoder;
        CodecContext _videoDecoder;

        WriteableBitmap _bitmap;

        public MainWindow()
        {
            InitializeComponent();

            _screenCapture = new DirectScreenCapture(0);

            _videoEncoder = new CodecContext(Codec.FindEncoderById(AVCodecID.H264))
            {
                Width = _screenCapture.ScreenWidth,
                Height = _screenCapture.ScreenHeight,
                Framerate = new AVRational(1, 30),
                TimeBase = new AVRational(1, 30),
                PixelFormat = AVPixelFormat.Yuv420p,
                MaxBFrames = 0,
                GopSize = 10,
                BitRate = 8000000
            };

            _videoDecoder = new(Codec.FindDecoderById(AVCodecID.H264))
            {
                Width = 2560,
                Height = 1440,
                PixelFormat = AVPixelFormat.Yuv420p,
            };

            _bitmap = new WriteableBitmap(_screenCapture.ScreenWidth, _screenCapture.ScreenHeight, 96, 96, PixelFormats.Bgra32, null);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {

            var rgbFrame = new Sdcb.FFmpeg.Utils.Frame();
            var yuvFrame = new Sdcb.FFmpeg.Utils.Frame();
            var yuvFrameForDecoding = new Sdcb.FFmpeg.Utils.Frame();
            var packetRef = new Packet();
            var videoFrameConverter = new VideoFrameConverter();

            Task.Run(() =>
            {
                _videoEncoder.Open(null, new MediaDictionary
                {
                    ["crf"] = "30",
                    ["tune"] = "zerolatency",
                    ["preset"] = "veryfast"
                    //["preset"] = "fast",
                    //["tune"] = "ull",
                });
                _videoDecoder.Open();

                while (true)
                {
                    _screenCapture.Capture();

                    rgbFrame.Width = _videoEncoder.Width;
                    rgbFrame.Height = _videoEncoder.Height;
                    rgbFrame.Format = (int)AVPixelFormat.Bgra;
                    rgbFrame.Data[0] = _screenCapture.DataPointer;
                    rgbFrame.Linesize[0] = _screenCapture.Stride;

                    yuvFrame.Width = _videoEncoder.Width;
                    yuvFrame.Height = _videoEncoder.Height;
                    yuvFrame.Format = (int)AVPixelFormat.Yuv420p;

                    yuvFrame.EnsureBuffer();
                    yuvFrame.MakeWritable();
                    videoFrameConverter.ConvertFrame(rgbFrame, yuvFrame);

                    foreach (var packet in _videoEncoder.EncodeFrame(yuvFrame, packetRef))
                    {
                        _videoDecoder.SendPacket(packet);

                        var decodeResult = _videoDecoder.ReceiveFrame(yuvFrameForDecoding);
                        if (decodeResult == CodecResult.Success)
                        {
                            using var rgbFrameForDecoding = new Sdcb.FFmpeg.Utils.Frame();
                            rgbFrameForDecoding.Width = yuvFrameForDecoding.Width;
                            rgbFrameForDecoding.Height = yuvFrameForDecoding.Height;
                            rgbFrameForDecoding.Format = (int)AVPixelFormat.Bgra;

                            rgbFrameForDecoding.EnsureBuffer();
                            rgbFrameForDecoding.MakeWritable();
                            videoFrameConverter.ConvertFrame(yuvFrameForDecoding, rgbFrameForDecoding);

                            unsafe
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    _bitmap.WritePixels(new Int32Rect(0, 0, _bitmap.PixelWidth, _bitmap.PixelHeight), rgbFrameForDecoding.Data[0], _screenCapture.ScreenHeight * _screenCapture.Stride, _screenCapture.Stride);
                                    _image.Source = _bitmap;
                                    //InvalidateVisual();
                                });
                            }
                        }
                    }
                }
            });
        }
    }
}