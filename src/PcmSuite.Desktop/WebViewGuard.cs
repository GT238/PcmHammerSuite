using Avalonia.Threading;
using PcmSuite.Tuning.Core.Models;

namespace PcmSuite;

/// <summary>
/// Avalonia's Windows web view can report a finished page load with an empty address if the page was replaced or left (a quick tab change)
/// while it was still loading, and that error closes the whole program. This lets that one error pass: it is written to the crash log
/// (marked as ignored) and the program carries on. Every other error is left alone.
/// </summary>
internal static class WebViewGuard
{
    public static void Install(string appName)
    {
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            if (!IsKnownWebViewError(e.Exception))
            {
                return;
            }

            CrashLog.Write(appName, e.Exception, "web view error (ignored: the program kept running)");
            e.Handled = true;
        };
    }

    internal static bool IsKnownWebViewError(Exception exception) =>
        exception is UriFormatException && (exception.StackTrace?.Contains("WebView2", StringComparison.Ordinal) ?? false);
}
