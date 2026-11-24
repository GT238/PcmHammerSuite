using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PcmSuite.Tuning.Core.Models;

namespace PcmSuite;

/// <summary>
/// The About tab: which build is running (version, the PcmSuite and PCM Hammer commits, runtime and system). Reporting a problem and the
/// crash logs are on the Logs tab. Shared by the full suite and the standalone Tuner.
/// </summary>
public sealed class AboutView : UserControl
{
    public AboutView(string appName)
    {
        TextBlock title = new() { Text = appName, FontSize = 24, FontWeight = FontWeight.SemiBold };
        TextBlock version = new() { Text = $"Version {BuildInfo.Version} (prototype)", FontSize = 14, Opacity = 0.8 };

        SelectableTextBlock details = new()
        {
            Text = BuildInfo.Describe(appName),
            FontFamily = new FontFamily("Consolas, Menlo, DejaVu Sans Mono, monospace"),
            TextWrapping = TextWrapping.Wrap,
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
                        Text = "To report a problem, use the Logs tab.",
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 14, 0, 0),
                        Opacity = 0.7,
                    },
                },
            },
        };
    }
}
