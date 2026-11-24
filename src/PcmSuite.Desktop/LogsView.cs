using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using PcmSuite.Tuning.Core.Models;

namespace PcmSuite;

/// <summary>
/// The Logs tab, where a problem is reported from: one button copies the build details (version, commits, system), and below it are the
/// crash logs the program left behind. Pick a log to read exactly what would be sent, copy it, delete it, or open the folder.
/// Shared by the full suite and the standalone Tuner.
/// </summary>
public sealed class LogsView : UserControl
{
    private const int MaxShownCharacters = 200_000;

    /// <summary>One crash file in the list, shown by its date and time.</summary>
    private sealed record Item(string Path)
    {
        public override string ToString()
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(this.Path);      // crash_yyyyMMdd_HHmmss_fff
            string[] parts = name.Split('_');
            return parts.Length >= 3
                   && DateTime.TryParseExact(parts[1] + parts[2], "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime when)
                ? when.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                : name;
        }
    }

    private readonly ListBox list = new();
    private readonly SelectableTextBlock viewer = new()
    {
        FontFamily = new FontFamily("Consolas, Menlo, DejaVu Sans Mono, monospace"),
        TextWrapping = TextWrapping.NoWrap,
        Margin = new Thickness(8),
    };

    private readonly TextBlock heading = new() { FontWeight = FontWeight.SemiBold };
    private readonly TextBlock status = new() { Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
    private readonly Button copy = new() { Content = "Copy log", IsEnabled = false };
    private readonly Button delete = new() { Content = "Delete", IsEnabled = false };

    private readonly string appName;

    public LogsView(string appName)
    {
        this.appName = appName;

        Button copyDetails = new() { Content = "Copy build details for a bug report" };
        copyDetails.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(BuildInfo.Describe(this.appName) + CrashSummary());
                this.status.Text = "Copied the version, the builds and your system. Paste it into your message and say what you were doing.";
            }
        };

        Button refresh = new() { Content = "Refresh" };
        refresh.Click += (_, _) => this.Refresh();

        Button openFolder = new() { Content = "Open folder" };
        openFolder.Click += async (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(CrashLog.Folder);
                if (TopLevel.GetTopLevel(this) is { } top)
                {
                    await top.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(CrashLog.Folder));
                }
            }
            catch (Exception ex)
            {
                this.status.Text = "Could not open the folder: " + ex.Message;
            }
        };

        this.copy.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard && this.viewer.Text is { Length: > 0 } text)
            {
                await clipboard.SetTextAsync(text);
                this.status.Text = "Copied the log. Paste it into your message.";
            }
        };

        this.delete.Click += (_, _) =>
        {
            if (this.list.SelectedItem is not Item item)
            {
                return;
            }

            try
            {
                File.Delete(item.Path);
                this.status.Text = "Deleted " + System.IO.Path.GetFileName(item.Path);
            }
            catch (Exception ex)
            {
                this.status.Text = "Could not delete it: " + ex.Message;
            }

            this.Refresh(keepStatus: true);
        };

        this.list.SelectionChanged += (_, _) => this.ShowSelected();

        StackPanel buttons = new() { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        buttons.Children.Add(this.copy);
        buttons.Children.Add(this.delete);
        buttons.Children.Add(openFolder);
        buttons.Children.Add(refresh);

        DockPanel right = new() { Margin = new Thickness(12, 0, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Top);
        right.Children.Add(buttons);
        right.Children.Add(new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = this.viewer,
            },
        });

        DockPanel left = new() { Width = 260 };
        DockPanel.SetDock(this.heading, Dock.Top);
        left.Children.Add(this.heading);
        this.heading.Margin = new Thickness(0, 0, 0, 8);
        left.Children.Add(this.list);

        // Top row: the report button. Below it the crash logs, and a status line at the bottom.
        StackPanel report = new() { Spacing = 6, Margin = new Thickness(0, 0, 0, 16) };
        report.Children.Add(new TextBlock { Text = "Reporting a problem", FontWeight = FontWeight.SemiBold });
        report.Children.Add(new TextBlock
        {
            Text = "Copy the build details and paste them into your message. If the program closed by itself, also send the newest crash log below.",
            TextWrapping = TextWrapping.Wrap,
        });
        report.Children.Add(copyDetails);

        Grid body = new() { Margin = new Thickness(16) };
        body.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        body.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        body.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        body.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Grid.SetColumnSpan(report, 2);
        Grid.SetRow(left, 1);
        Grid.SetRow(right, 1);
        Grid.SetColumn(right, 1);
        Grid.SetRow(this.status, 2);
        Grid.SetColumnSpan(this.status, 2);
        this.status.Margin = new Thickness(0, 8, 0, 0);
        body.Children.Add(report);
        body.Children.Add(left);
        body.Children.Add(right);
        body.Children.Add(this.status);

        this.Content = body;
        this.AttachedToVisualTree += (_, _) => this.Refresh();
    }

    /// <summary>Reload the list (called when the tab is shown).</summary>
    public void Refresh(bool keepStatus = false)
    {
        string[] files = CrashLog.Files();
        this.list.ItemsSource = files.Select(f => new Item(f)).ToList();
        this.heading.Text = files.Length == 0 ? "Crash logs" : $"Crash logs ({files.Length})";

        if (files.Length > 0)
        {
            this.list.SelectedIndex = 0;
        }
        else
        {
            this.ShowSelected();
        }

        if (!keepStatus)
        {
            this.status.Text = files.Length == 0
                ? "No crash logs. If the program ever closes by itself, its log will be saved in " + CrashLog.Folder
                : "Saved in " + CrashLog.Folder + ". Send the newest one with your bug report; it holds the error and the build details, not your BIN files.";
        }
    }

    // Added to the copied details so a report says how many crashes there have been.
    private static string CrashSummary()
    {
        string[] files = CrashLog.Files();
        return files.Length == 0 ? string.Empty : $"Crash logs: {files.Length}, newest {System.IO.Path.GetFileName(files[0])}{Environment.NewLine}";
    }

    private void ShowSelected()
    {
        bool has = this.list.SelectedItem is Item;
        this.copy.IsEnabled = has;
        this.delete.IsEnabled = has;

        if (this.list.SelectedItem is not Item item)
        {
            this.viewer.Text = string.Empty;
            return;
        }

        try
        {
            string text = File.ReadAllText(item.Path);
            this.viewer.Text = text.Length > MaxShownCharacters ? text.Substring(0, MaxShownCharacters) + Environment.NewLine + "(cut off)" : text;
        }
        catch (Exception ex)
        {
            this.viewer.Text = "Could not read this file: " + ex.Message;
        }
    }
}
