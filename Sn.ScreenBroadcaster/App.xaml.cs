using System.Configuration;
using System.Data;
using System.Globalization;
using System.Windows;
using Windows.Win32;

namespace Sn.ScreenBroadcaster;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    static App()
    {
        // custom dll loading logic for .NET Framework
#if !NETCOREAPP
        bool isWin64 = IntPtr.Size == 8;
        var path = Environment.GetEnvironmentVariable("PATH");
        var absFolderPath = AppContext.BaseDirectory;

        if (isWin64)
        {
            var dllFolderPath = System.IO.Path.Combine(absFolderPath, @"dll\x64");
            Environment.SetEnvironmentVariable("PATH", $"{dllFolderPath};{path}");
        }
        else
        {
            var dllFolderPath = System.IO.Path.Combine(absFolderPath, @"dll\x86");
            Environment.SetEnvironmentVariable("PATH", $"{dllFolderPath};{path}");
        }
#endif
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Any(str => str.Equals("--console", StringComparison.OrdinalIgnoreCase)))
        {
            PInvoke.AllocConsole();
        }

        base.OnStartup(e);

        if (Resources is null)
        {
            Resources = new ResourceDictionary();
        }

        var currentCulture = CultureInfo.CurrentCulture;
        if (currentCulture.Name == "zh-CN" ||
            currentCulture.Name == "zh-Hans")
        {
            Resources.MergedDictionaries.Add(
                new ResourceDictionary()
                {
                    Source = new Uri("/Translations/ZH_HANS.xaml", UriKind.Relative)
                });
        }
    }
}

