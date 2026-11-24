using Avalonia;

namespace PcmSuite;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        PcmSuite.Tuning.Core.Models.CrashLog.Install("PcmSuite");
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
