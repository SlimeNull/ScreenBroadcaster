using System.Runtime.InteropServices;
using LibScreenCapture;
using SkiaSharp;
using Spectre.Console;


IScreenCapture capture = new DirectScreenCapture(0);

for (int i = 0; ; i++)
{
    bool ok = capture.Capture(TimeSpan.FromSeconds(1f));

    SKBitmap bitmap = new SKBitmap(capture.ScreenWidth, capture.ScreenHeight, SKColorType.Bgra8888, SKAlphaType.Opaque);
    var bitmapPixelsPtr = bitmap.GetPixels();

    unsafe
    {
        for (int y = 0; y < capture.ScreenHeight; y++)
        {
            NativeMemory.Copy((void*)(capture.DataPointer + capture.Stride * y), (void*)(bitmapPixelsPtr + bitmap.RowBytes * y), (nuint)Math.Min(capture.Stride, bitmap.RowBytes));
        }
    }

    //using var output = File.Create($"output{i}.png");

    var image = new CanvasImage(bitmap.Encode(SKEncodedImageFormat.Jpeg, 20).AsStream());
    Console.SetCursorPosition(0, 0);
    AnsiConsole.Write(image);

    Console.WriteLine($"Capture ok: {ok}, Index: {i}");
}

