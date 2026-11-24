using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using PcmSuite.Tuning.Core.Models;

namespace PcmSuite;

/// <summary>
/// The About tab: which build is running (version and the PcmSuite and PCM Hammer commits) and one button that copies everything a bug
/// report needs. Crash logs are on the Logs tab. Shared by the full suite and the standalone Tuner.
/// </summary>
public sealed class AboutView : UserControl
{
    private readonly string appName;
    private readonly TextBlock status = new() { Opacity = 0.7 };

    public AboutView(string appName)
    {
        this.appName = appName;

        TextBlock title = new() { Text = appName, FontSize = 24, FontWeight = FontWeight.SemiBold };
        TextBlock version = new() { Text = $"Version {BuildInfo.Version} (prototype)", FontSize = 14, Opacity = 0.8 };

        SelectableTextBlock details = new()
        {
            Text = BuildInfo.Describe(appName),
            FontFamily = new FontFamily("Consolas, Menlo, DejaVu Sans Mono, monospace"),
            TextWrapping = TextWrapping.Wrap,
        };

        Button copy = new() { Content = "Copy details for a bug report" };
        copy.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(BuildInfo.Describe(this.appName) + CrashSummary());
                this.status.Text = "Copied. Paste it into your message.";
            }
        };

        this.Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 10,
                MaxWidth = 760,
                HorizontalAlignment = HorizontalAlignment.Left,
                Children =
                {
                    title,
                    version,
                    new TextBlock { Text = "This build", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 14, 0, 0) },
                    details,
                    new TextBlock
                    {
                        Text = "Include these details when you report a problem. If the program closed by itself, see the Logs tab.",
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 14, 0, 0),
                    },
                    copy,
                    this.status,
                },
            },
        };
    }

    // Added to the copied details so a report says how many crashes there have been.
    private static string CrashSummary()
    {
        string[] files = CrashLog.Files();
        return files.Length == 0 ? string.Empty : $"Crash logs: {files.Length}, newest {Path.GetFileName(files[0])}{Environment.NewLine}";
    }
}
