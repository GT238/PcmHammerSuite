using Avalonia.Threading;

namespace PcmSuite;

/// <summary>
/// Avalonia's Windows web view can report a finished page load with an empty address if the page was replaced or left (a quick tab change)
/// while it was still loading, and that error closes the whole program. It happens whenever a page loads, so it is not a crash worth
/// reporting: this lets that one error pass without writing a crash log, and the program carries on. Every other error is left alone.
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

            System.Diagnostics.Trace.WriteLine($"{appName}: ignored a web view error: {e.Exception.Message}");
            e.Handled = true;
        };
    }

    internal static bool IsKnownWebViewError(Exception exception) =>
        exception is UriFormatException && (exception.StackTrace?.Contains("WebView2", StringComparison.Ordinal) ?? false);
}
