using Avalonia;

namespace PcmSuite;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        PcmSuite.Tuning.Core.Models.CrashLog.Install("PcmSuiteTuner");
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
