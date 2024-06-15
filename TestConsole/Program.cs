using System.Runtime.InteropServices;
using LibScreenCapture;
using SkiaSharp;

IScreenCapture capture = new DirectScreenCapture(0);

for (int i = 0; ; i++)
{
    bool ok = capture.Capture(TimeSpan.FromSeconds(0.1f));

    SKBitmap bitmap = new SKBitmap(capture.Width, capture.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
    var bitmapPixelsPtr = bitmap.GetPixels();

    unsafe
    {
        for (int y = 0; y < capture.Height; y++)
        {
            NativeMemory.Copy((void*)(capture.DataPointer + capture.Stride * y), (void*)(bitmapPixelsPtr + bitmap.RowBytes * y), (nuint)Math.Min(capture.Stride, bitmap.RowBytes));
        }
    }

    using var output = File.Create($"output{i}.png");
    bitmap.Encode(output, SKEncodedImageFormat.Png, 0);

    // See https://aka.ms/new-console-template for more information
    Console.WriteLine($"Capture ok: {ok}");
}

