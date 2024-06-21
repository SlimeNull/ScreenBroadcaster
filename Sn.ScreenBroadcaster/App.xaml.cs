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

