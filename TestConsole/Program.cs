﻿using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using LibScreenCapture;
using Sdcb.FFmpeg.Codecs;
using SharpDX.DXGI;
using SkiaSharp;
using Sn.ScreenBroadcaster.Utilities;
using Spectre.Console;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

var encoders = Codec.FindEncoders(Sdcb.FFmpeg.Raw.AVCodecID.H264);

var factory = new Factory1();
var adapters = factory.Adapters;

bool hasNvidiaGPU = false;
bool hasAmdGPU = false;
foreach (var adapter in adapters)
{
    Console.WriteLine(adapter);
}




unsafe
{
    CursorLoader cursorLoader = new();
    cursorLoader.EnableCache = false;

    while (true)
    {
        var cursor = cursorLoader.GetCurrentCursor();
        if (cursor?.DirectBitmap is null)
            continue;

        var image = new CanvasImage(cursor.Value.DirectBitmap.Encode(SKEncodedImageFormat.Png, 100).AsStream());

        Console.SetCursorPosition(0, 0);
        AnsiConsole.Write(image);
    }

    //HCURSOR h = PInvoke.GetCursor();

    //var cursorInfo = new CURSORINFO();
    //cursorInfo.cbSize = (uint)sizeof(CURSORINFO);
    //var isOk = PInvoke.GetCursorInfo(ref cursorInfo);


    //var xHotspot = iconInfo.xHotspot;
    //var yHotspot = iconInfo.yHotspot;
}

////PInvoke.LoadCursor()

//Bitmap bitmap = new(128, 128);
//var g = Graphics.FromImage(bitmap);
//Cursor.Current?.Draw(g, new Rectangle(0, 0, 64, 64));
//bitmap.Save("test.jpg");



//IScreenCapture capture = new DirectScreenCapture(0);

//for (int i = 0; ; i++)
//{
//    bool ok = capture.Capture(TimeSpan.FromSeconds(1f));

//    SKBitmap bitmap = new SKBitmap(capture.ScreenWidth, capture.ScreenHeight, SKColorType.Bgra8888, SKAlphaType.Opaque);
//    var bitmapPixelsPtr = bitmap.GetPixels();

//    unsafe
//    {
//        for (int y = 0; y < capture.ScreenHeight; y++)
//        {
//            NativeMemory.Copy((void*)(capture.DataPointer + capture.Stride * y), (void*)(bitmapPixelsPtr + bitmap.RowBytes * y), (nuint)Math.Min(capture.Stride, bitmap.RowBytes));
//        }
//    }

//    //using var output = File.Create($"output{i}.png");

//    var image = new CanvasImage(bitmap.Encode(SKEncodedImageFormat.Jpeg, 20).AsStream());
//    Console.SetCursorPosition(0, 0);
//    AnsiConsole.Write(image);

//    Console.WriteLine($"Capture ok: {ok}, Index: {i}");
//}

