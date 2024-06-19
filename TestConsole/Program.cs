using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using LibScreenCapture;
using SkiaSharp;
using Spectre.Console;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;


unsafe
{
    CursorLoader cursor = new();
    cursor.Initialize();

    while (true)
    {
        var bitmap = cursor.GetCursor();

        var image = new CanvasImage(bitmap.Encode(SKEncodedImageFormat.Png, 100).AsStream());
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

