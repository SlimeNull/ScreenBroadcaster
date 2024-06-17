using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using LibCommon;
using LibScreenCapture;
using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Raw;
using Sdcb.FFmpeg.Swscales;
using Sdcb.FFmpeg.Toolboxs.Extensions;
using Sdcb.FFmpeg.Utils;

namespace TestForm
{
    public partial class MainForm : Form
    {
        BufferedGraphics? _bufferedGraphics;

        IScreenCapture _screenCapture;
        CodecContext _videoEncoder;
        CodecContext _videoDecoder;

        public MainForm()
        {
            InitializeComponent();

            _screenCapture = new DirectScreenCapture(0);

            _videoEncoder = new CodecContext(FFmpegUtilities.FindBestEncoder(AVCodecID.H264))
            {
                Width = _screenCapture.Width,
                Height = _screenCapture.Height,
                Framerate = new AVRational(1, 30),
                TimeBase = new AVRational(1, 30),
                PixelFormat = AVPixelFormat.Yuv420p,
                GopSize = 10,
                //BitRate = 80000
            };

            _videoDecoder = new(FFmpegUtilities.FindBestDecoder(AVCodecID.H264))
            {
                Width = 2560,
                Height = 1440,
                PixelFormat = AVPixelFormat.Yuv420p,
            };
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            _bufferedGraphics = BufferedGraphicsManager.Current.Allocate(CreateGraphics(), ClientRectangle);

            var rgbFrame = new Frame();
            var yuvFrame = new Frame();
            var yuvFrameForDecoding = new Frame();
            var packetRef = new Packet();
            var videoFrameConverter = new VideoFrameConverter();

            Task.Run(() =>
            {
                var bitmap = new Bitmap(2560, 1440, System.Drawing.Imaging.PixelFormat.Format32bppRgb);

                _videoEncoder.Open(null, new MediaDictionary
                {
                    ["preset"] = "fast",
                    ["tune"] = "ull",
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
                            using var rgbFrameForDecoding = new Frame();
                            rgbFrameForDecoding.Width = yuvFrameForDecoding.Width;
                            rgbFrameForDecoding.Height = yuvFrameForDecoding.Height;
                            rgbFrameForDecoding.Format = (int)AVPixelFormat.Bgra;

                            rgbFrameForDecoding.EnsureBuffer();
                            rgbFrameForDecoding.MakeWritable();
                            videoFrameConverter.ConvertFrame(yuvFrameForDecoding, rgbFrameForDecoding);

                            unsafe
                            {
                                var bmpData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
                                NativeMemory.Copy((void*)rgbFrameForDecoding.Data[0], (void*)bmpData.Scan0, (nuint)(bitmap.Height * bmpData.Stride));
                                bitmap.UnlockBits(bmpData);
                            }

                            if (_bufferedGraphics is not null)
                            {
                                LayoutUtilities.Uniform(ClientSize.Width, ClientSize.Height, bitmap.Width, bitmap.Height, out var offsetX, out var offsetY, out var imageDrawWidth, out var imageDrawHeight);

                                _bufferedGraphics.Graphics.DrawImage(bitmap, offsetX, offsetY, imageDrawWidth, imageDrawHeight);
                                _bufferedGraphics.Render();
                            }
                        }
                    }
                }
            });
        }

        private void MainForm_Resize(object sender, EventArgs e)
        {
            _bufferedGraphics = BufferedGraphicsManager.Current.Allocate(CreateGraphics(), ClientRectangle);

            var oldBufferedGraphics = _bufferedGraphics;
            if (oldBufferedGraphics is not null)
                oldBufferedGraphics.Dispose();
        }
    }
}
