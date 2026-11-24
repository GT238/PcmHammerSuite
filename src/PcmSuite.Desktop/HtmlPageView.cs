using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace PcmSuite;

/// <summary>
/// Shows an HTML page (Help, Credits). One web view is created and reused, so a page is never replaced while an older one is still
/// loading. Because the built-in web view does not work everywhere (on some Linux desktops it stays blank), the page can always be
/// opened in the system browser instead, and a notice offers that if the web view has not reported a loaded page.
/// </summary>
public sealed class HtmlPageView : UserControl
{
    private readonly ContentControl host = new();
    private readonly Border notice;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(4) };
    private NativeWebView? web;
    private bool loaded;
    private int pageNumber;
    private string? currentFile;
    private string title = "page";
    private string html = string.Empty;

    public HtmlPageView()
    {
        Button open = new() { Content = "Open in your browser", HorizontalAlignment = HorizontalAlignment.Right };
        open.Click += async (_, _) => await this.OpenInBrowserAsync();

        TextBlock message = new()
        {
            Text = "If this page is blank, the built-in viewer does not work on this system. Use the button to read it in your web browser.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };

        this.notice = new Border
        {
            IsVisible = false,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 6),
            CornerRadius = new CornerRadius(4),
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(40, 232, 168, 56)),
            Child = message,
        };

        DockPanel strip = new() { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(open, Dock.Right);
        strip.Children.Add(open);

        DockPanel layout = new();
        DockPanel.SetDock(strip, Dock.Top);
        DockPanel.SetDock(this.notice, Dock.Top);
        layout.Children.Add(strip);
        layout.Children.Add(this.notice);
        layout.Children.Add(this.host);
        this.Content = layout;

        this.timer.Tick += (_, _) =>
        {
            this.timer.Stop();
            this.notice.IsVisible = this.web != null && !this.loaded;
        };
    }

    /// <summary>Show a page. The title only names the file when it is opened in the browser.</summary>
    public void Show(string pageHtml, string pageTitle)
    {
        this.title = Regex.Replace(pageTitle, "[^A-Za-z0-9_-]", "_");

        // The same page again (the tab was shown again): leave it as it is. Navigating to the same address would not load anything new.
        if (this.web != null && pageHtml == this.html)
        {
            return;
        }

        this.html = pageHtml;
        this.loaded = false;
        this.notice.IsVisible = false;
        this.timer.Stop();

        try
        {
            if (this.web == null)
            {
                this.web = new NativeWebView();
                this.web.NavigationCompleted += (_, _) =>
                {
                    this.loaded = true;
                    this.notice.IsVisible = false;
                };
                this.host.Content = this.web;
            }

            // Load the page from a temporary file instead of a data: address. The web view reports an empty address for a data: page
            // after it loads (which used to close the program) and some Linux web views will not draw one.
            string folder = Path.Combine(Path.GetTempPath(), "PcmSuite-pages");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"{this.title}-{++this.pageNumber}.html");
            File.WriteAllText(path, pageHtml);
            string? previous = this.currentFile;
            this.currentFile = path;
            this.web.Source = new Uri(path);
            this.timer.Start();

            if (previous != null)
            {
                try { File.Delete(previous); } catch (Exception) { }
            }
        }
        catch (Exception)
        {
            // No web view at all: show the same text without formatting.
            this.web = null;
            this.host.Content = new ScrollViewer
            {
                Content = new SelectableTextBlock
                {
                    Text = Regex.Replace(Regex.Replace(pageHtml, "<style.*?</style>", string.Empty, RegexOptions.Singleline), "<[^>]+>", string.Empty),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Margin = new Thickness(16),
                },
            };
            this.notice.IsVisible = true;
        }
    }

    private async Task OpenInBrowserAsync()
    {
        try
        {
            string folder = Path.Combine(Path.GetTempPath(), "PcmSuite-pages");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, this.title + "-to-open.html");
            File.WriteAllText(path, this.html);

            if (TopLevel.GetTopLevel(this) is { } top)
            {
                await top.Launcher.LaunchFileInfoAsync(new FileInfo(path));
            }
        }
        catch (Exception)
        {
            this.notice.IsVisible = true;
        }
    }
}
