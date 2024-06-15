using LibScreenCapture;

IScreenCapture screenCapture = new GdiScreenCapture();
int captureCounter = 0;

_ = Task.Run(() =>
{
    while (true)
    {
        var ok = screenCapture.Capture(TimeSpan.FromMilliseconds(0));

        if (ok)
        {
            captureCounter++;
        }
    }
});

_ = Task.Run(async () =>
{
    while (true)
    {
        await Task.Delay(1000);
        Console.WriteLine($"{captureCounter}/s");
        captureCounter = 0;
    }
});

await Task.Delay(-1);
