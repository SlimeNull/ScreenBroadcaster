using System.Configuration;
using System.Data;
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
    }
}

