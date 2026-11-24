using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using PcmSuite.Tune;

namespace PcmSuite;

/// <summary>
/// The Help tab: a topic list over one web view. The suite and Tuner pages are PcmSuite's own; the third is PCM Hammer's help
/// page, taken straight from the PcmHammer source tree at build time (so it follows the submodule).
/// </summary>
public sealed class HelpView : UserControl
{
    // PCM Hammer's page is only present in the full suite; the standalone Tuner leaves it (and the questions about it) out.
    private static readonly bool HasHammerHelp = File.Exists(Path.Combine(AppContext.BaseDirectory, "Help", "PcmHammer-help.html"));

    private static readonly (string Key, string Title, string File)[] Topics = new (string Key, string Title, string File)[]
    {
        ("welcome", "Welcome to PcmSuite", "Welcome.html"),
        ("hammer", "Flash and Logger (PCM Hammer's help)", "PcmHammer-help.html"),
        ("tuner", "The Tuner (manual)", "Tuner.html"),
    }.Where(t => t.Key != "hammer" || HasHammerHelp).ToArray();

    /// <summary>One line in the "How do I...?" list or the search results.</summary>
    private sealed record Entry(string Title, string Detail, string Topic, string? Anchor);

    // Common tasks, each leading to the section that answers it.
    private static readonly Entry[] Tasks = new Entry[]
    {
        new("Start a new project from a read or a BIN", "Make a vehicle folder for reads, tunes, logs and notes", "tuner", "projects"),
        new("Open a BIN and get its XDF", "How the matching XDF is found", "tuner", "opening"),
        new("Fix \"No XDF found\" or a protected XDF", "Why some XDF files cannot be read", "tuner", "problems"),
        new("Edit a table", "Select cells, type values, nudge, fill, scale, interpolate, smooth", "tuner", "grid"),
        new("Undo or redo a change", "Undo, redo and the history list", "tuner", "undo"),
        new("See what I have changed compared with stock", "Compare with the base BIN", "tuner", "compare"),
        new("Keep reference BINs (factory files and others) in one place and compare with them quickly", "The Reference BINs folder and Compare with: a matching BIN", "tuner", "reference"),
        new("See which tables another tune changed, and copy them into mine", "The compare workbench", "tuner", "workbench"),
        new("Look at the BIN byte by byte", "The hex view: go to an address, search, see differences", "tuner", "hex"),
        new("See where a logged drive went on a table", "The log map", "tuner", "log-map"),
        new("Save my tune and fix the checksums", "Save As, the validity check and Send to Flash", "tuner", "saving"),
        new("Keep a record of every edit", "The change log", "tuner", "records"),
        new("Write notes about a table", "Notes, pins and tags", "tuner", "meta"),
        new("Get VE table corrections from a log", "The VE wizard", "tuner", "ve-wizard"),
        new("Choose what to log for tuning", "Ready-made logging profiles", "tuner", "profiles"),
        new("Record a drive with the Logger", "A typical session, step by step", "welcome", "workflow"),
        new("Watch which cell the engine is in", "Live trace", "tuner", "live"),
        new("Use a patch or swap segments", "Patches and segment swap", "tuner", "patches"),
        new("Open files by dragging them in", "Drag and drop and the command line", "tuner", "drop"),
        new("See the keyboard shortcuts", "Grid keys", "tuner", "keys"),
        new("Read or write the PCM", "PCM Hammer's basic operations", "hammer", "basics"),
        new("Find out which adapters and PCMs are supported", "PCM Hammer's supported devices", "hammer", "devices"),
        new("Get the kernel files", "Why they are not included", "welcome", "kernels"),
        new("Understand why only one tab can use the adapter", "One adapter at a time", "welcome", "adapter"),
        new("Check what to do before flashing", "A safety checklist", "welcome", "safety"),
    }.Where(e => e.Topic != "hammer" || HasHammerHelp).ToArray();

    private readonly ComboBox picker = new() { HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 260 };
    private readonly TextBox search = new()
    {
        PlaceholderText = "Ask a question or type a few words, for example: save the file",
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    private readonly Expander quick = new() { Header = "How do I...?", IsExpanded = false, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly ListBox list = new() { MaxHeight = 280 };
    private readonly HelpSearch index = new();
    private IReadOnlyList<Entry> shown = Tasks;
    private readonly ContentControl page = new();
    private bool showing;
    private string currentKey = "welcome";
    private string? currentAnchor;

    public HelpView()
    {
        foreach ((string _, string title, string _) in Topics)
        {
            this.picker.Items.Add(title);
        }

        this.picker.SelectionChanged += (_, _) =>
        {
            if (!this.showing && this.picker.SelectedIndex >= 0)
            {
                this.Render(Topics[this.picker.SelectedIndex].Key, null);
            }
        };

        // The search covers every page; it is built once, from the same files the pages show.
        foreach ((string key, string title, string file) in Topics)
        {
            this.index.AddPage(key, title, Read(Path.Combine(AppContext.BaseDirectory, "Help", file)));
        }

        this.list.ItemTemplate = new FuncDataTemplate<Entry>((entry, _) =>
            entry == null
                ? null
                : new StackPanel
                {
                    Margin = new Thickness(2, 4),
                    Children =
                    {
                        new TextBlock { Text = entry.Title, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                        new TextBlock { Text = entry.Detail, Opacity = 0.7, FontSize = 12, TextWrapping = TextWrapping.Wrap, IsVisible = entry.Detail.Length > 0 },
                    },
                });
        this.list.ItemsSource = Tasks;
        this.list.SelectionChanged += (_, _) =>
        {
            if (this.list.SelectedItem is Entry entry && entry.Topic.Length > 0)
            {
                this.Open(entry);
            }
        };

        this.quick.Content = this.list;
        this.search.TextChanged += (_, _) => this.UpdateResults();
        this.search.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter && this.shown.Count > 0 && this.shown[0].Topic.Length > 0)
            {
                this.Open(this.shown[0]);
                e.Handled = true;
            }
            else if (e.Key == Avalonia.Input.Key.Escape)
            {
                this.search.Text = string.Empty;
            }
        };

        DockPanel layout = new() { Margin = new Thickness(8) };
        StackPanel top = new() { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        top.Children.Add(new TextBlock { Text = "Topic", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 });
        top.Children.Add(this.picker);
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(this.search, Dock.Top);
        this.search.Margin = new Thickness(0, 0, 0, 8);
        DockPanel.SetDock(this.quick, Dock.Top);
        this.quick.Margin = new Thickness(0, 0, 0, 8);
        layout.Children.Add(top);
        layout.Children.Add(this.search);
        layout.Children.Add(this.quick);
        layout.Children.Add(this.page);
        this.Content = layout;

        this.Show("welcome", null);
    }

    private void Open(Entry entry)
    {
        this.Show(entry.Topic, entry.Anchor);
        this.list.SelectedItem = null;   // so the same line can be picked again
        this.quick.IsExpanded = false;
    }

    /// <summary>Typing in the box turns the "How do I...?" list into the matching sections; empty brings the list back.</summary>
    private void UpdateResults()
    {
        string query = this.search.Text?.Trim() ?? string.Empty;
        if (query.Length == 0)
        {
            this.shown = Tasks;
            this.quick.Header = "How do I...?";
        }
        else
        {
            IReadOnlyList<HelpHit> hits = this.index.Search(query);
            if (hits.Count == 0)
            {
                this.shown = new[] { new Entry($"Nothing found for \"{query}\"", "Try other words, or pick a topic above.", string.Empty, null) };
                this.quick.Header = "No results";
            }
            else
            {
                this.shown = hits.Select(h => new Entry(h.Section.Title, h.Section.TopicTitle + ": " + h.Snippet, h.Section.Topic, h.Section.Anchor)).ToList();
                this.quick.Header = $"Results ({hits.Count}) - press Enter for the first";
            }

            this.quick.IsExpanded = true;
        }

        this.list.ItemsSource = this.shown;
    }

    /// <summary>Show a topic ("welcome", "tuner" or "hammer"), scrolled to an anchor in it when one is given.</summary>
    public void Show(string topic, string? anchor)
    {
        int index = Array.FindIndex(Topics, t => t.Key == topic);
        if (index < 0)
        {
            index = 0;
        }

        this.showing = true;
        this.picker.SelectedIndex = index;
        this.showing = false;
        this.Render(Topics[index].Key, anchor);
    }

    /// <summary>Show the current topic again from its start (called when the Help tab is opened).</summary>
    public void Refresh() => this.Render(this.currentKey, this.currentAnchor);

    private void Render(string key, string? anchor)
    {
        this.currentKey = key;
        this.currentAnchor = anchor;
        string file = Topics.First(t => t.Key == key).File;
        string html = Wrap(Read(Path.Combine(AppContext.BaseDirectory, "Help", file)), anchor);
        try
        {
            this.page.Content = new NativeWebView
            {
                Source = new Uri("data:text/html;charset=utf-8," + Uri.EscapeDataString(html)),
            };
        }
        catch (Exception)
        {
            // No web view available: show the text without formatting.
            this.page.Content = new ScrollViewer
            {
                Content = new SelectableTextBlock
                {
                    Text = Regex.Replace(Regex.Replace(html, "<style.*?</style>", string.Empty, RegexOptions.Singleline), "<[^>]+>", string.Empty),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Margin = new Thickness(16),
                },
            };
        }
    }

    private static string Wrap(string document, string? anchor)
    {
        // A whole HTML document (PCM Hammer's page): keep only what is inside its <body>.
        Match body = Regex.Match(document, "<body[^>]*>(.*)</body>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        string content = body.Success ? body.Groups[1].Value : document;

        // An in-page link (href="#section") is treated by the web view as a navigation away from this inline page and
        // does nothing. Make it scroll to the section itself instead.
        content = Regex.Replace(
            content,
            @"<a\b([^>]*?)href=""#([A-Za-z0-9_-]+)""([^>]*)>",
            "<a$1href=\"#$2\" onclick=\"var t=document.getElementById('$2'); if (t) { t.scrollIntoView(); } return false;\"$3>",
            RegexOptions.IgnoreCase);
        // Always start where it was asked to: at the anchor, or at the very top.
        string jump = string.IsNullOrEmpty(anchor)
            ? "<script>window.addEventListener('load',function(){window.scrollTo(0,0);});</script>"
            : "<script>window.addEventListener('load',function(){var t=document.getElementById('" + Regex.Replace(anchor, "[^A-Za-z0-9_-]", string.Empty) + "'); if (t) { t.scrollIntoView(); }});</script>";

        return "<html><head><base target=\"_blank\"/><style>" +
               "* { font-family: sans-serif; } body { max-width: 52em; margin: 1em auto; padding: 0 1em; } " +
               "p, li, dd, dt { font-size: 0.9em; line-height: 1.45; } h1 { margin-top: 0.2em; } h3 { margin-top: 1.4em; } " +
               "h4 { margin-top: 1.1em; margin-bottom: 0.25em; } h4 + p, h4 + ul { margin-top: 0.2em; } " +
               "code { background: rgba(128,128,128,0.2); padding: 0 0.25em; border-radius: 3px; } " +
               "dt { font-weight: bold; margin-top: 0.5em; } dd { margin-left: 1.5em; } " +
               "@media (prefers-color-scheme: dark) { body { background: #202020; color: #e6e6e6; } a { color: #8ab4f8; } }" +
               "</style></head><body>" + content + jump + "</body></html>";
    }

    private static string Read(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : $"<p>Help file not found: {Path.GetFileName(path)}</p>";
        }
        catch (Exception ex)
        {
            return $"<p>Could not read {Path.GetFileName(path)}: {ex.Message}</p>";
        }
    }
}
