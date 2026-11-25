using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PcmSuite.Tuning.Core.Services;

namespace PcmSuite.Tune;

/// <summary>
/// "Find tables from a similar BIN": for a BIN that has no XDF of its own. The user names a BIN of a similar OS that does have a readable XDF;
/// the Tuner works out where that definition's tables are in this BIN, shows what it found and how sure it is, and only then adds them.
/// Built in code, like the other small pages.
/// </summary>
public sealed class DefinitionFinderView : OverlayPage
{
    private readonly TuneViewModel tune;
    private readonly Func<Task<string?>> pickBin;
    private readonly Func<Task<string?>> pickXdf;
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar busy = new() { IsIndeterminate = true, IsVisible = false };
    private readonly Button chooseXdf = new() { Content = "Choose its XDF...", IsVisible = false };
    private readonly StackPanel results = new() { Spacing = 8, IsVisible = false };
    private readonly TextBlock summary = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold };
    private readonly CheckBox includeInferred = new() { Content = "Also use tables placed by their neighbours (less certain)", IsChecked = true };
    private readonly ListBox list = new() { MaxHeight = 260 };
    private readonly Button use = new() { Content = "Use these tables", IsEnabled = false, MinWidth = 140 };

    private string? referenceBin;
    private FoundTables? found;

    public DefinitionFinderView(TuneViewModel tune, Func<Task<string?>> pickBin, Func<Task<string?>> pickXdf)
    {
        this.tune = tune;
        this.pickBin = pickBin;
        this.pickXdf = pickXdf;

        Button choose = new() { Content = "Choose a similar BIN..." };
        choose.Click += async (_, _) =>
        {
            string? path = await this.pickBin();
            if (path != null)
            {
                this.referenceBin = path;
                await this.AnalyseAsync(null);
            }
        };
        this.chooseXdf.Click += async (_, _) =>
        {
            string? xdf = await this.pickXdf();
            if (xdf != null && this.referenceBin != null)
            {
                await this.AnalyseAsync(xdf);
            }
        };

        this.includeInferred.IsCheckedChanged += (_, _) => this.UpdateUse();
        this.use.Click += (_, _) =>
        {
            if (this.found != null)
            {
                this.tune.ApplyFound(this.found, this.includeInferred.IsChecked == true);
            }

            this.RequestClose();
        };
        Button cancel = new() { Content = "Cancel", MinWidth = 90 };
        cancel.Click += (_, _) => this.RequestClose();

        this.results.Children.Add(this.summary);
        this.results.Children.Add(this.includeInferred);
        this.results.Children.Add(new TextBlock
        {
            Text = "● found by its values   ◐ placed because the tables around it moved by the same amount   ○ not placed",
            Opacity = 0.7,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
        });
        this.results.Children.Add(this.list);

        StackPanel form = new() { Spacing = 10 };
        form.Children.Add(new TextBlock { Text = "Find tables from a similar BIN", FontSize = 16, FontWeight = FontWeight.SemiBold });
        form.Children.Add(new TextBlock
        {
            Text = "For a BIN that has no XDF of its own. Pick a BIN of a similar operating system that you do have an XDF for. " +
                   "The Tuner compares the two BINs, works out where each of that XDF's tables is in this one, and shows you what it found before anything is added. " +
                   "Nothing in either BIN or in the XDF is changed.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            FontSize = 12,
        });
        form.Children.Add(new WrapPanel { Children = { Margin(choose), Margin(this.chooseXdf) } });
        form.Children.Add(this.busy);
        form.Children.Add(this.message);
        form.Children.Add(this.results);

        StackPanel buttons = new() { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { this.use, cancel } };
        Grid layout = new() { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 10 };
        layout.Children.Add(new ScrollViewer { Content = form, Padding = new Thickness(0, 0, 12, 0) });
        Grid.SetRow(buttons, 1);
        layout.Children.Add(buttons);
        this.Content = layout;
    }

    private static Control Margin(Control c)
    {
        c.Margin = new Thickness(0, 0, 8, 6);
        return c;
    }

    private async Task AnalyseAsync(string? xdf)
    {
        this.found = null;
        this.results.IsVisible = false;
        this.chooseXdf.IsVisible = false;
        this.use.IsEnabled = false;
        this.busy.IsVisible = true;
        this.message.Foreground = null;
        this.message.Text = "Comparing with " + Path.GetFileName(this.referenceBin) + "...";
        try
        {
            this.found = await this.tune.FindTablesAsync(this.referenceBin!, xdf);
            this.Show(this.found);
        }
        catch (NoReadableDefinitionException ex)
        {
            this.message.Text = ex.Message;
            this.message.Foreground = Brushes.Goldenrod;
            this.chooseXdf.IsVisible = true;
        }
        catch (Exception ex)
        {
            this.message.Text = "That did not work: " + ex.Message;
            this.message.Foreground = Brushes.IndianRed;
        }
        finally
        {
            this.busy.IsVisible = false;
        }
    }

    private void Show(FoundTables f)
    {
        MigrationResult r = f.Result;
        int content = r.Count(MatchKind.Content);
        int inferred = r.Count(MatchKind.Inferred);
        int none = r.Count(MatchKind.Unresolved);

        this.message.Text = $"Compared {f.ReferenceName} (using {f.XdfName}) with this BIN.";
        this.summary.Text = $"{r.Matches.Count} tables in that XDF: {content} found by their values, {inferred} placed by their neighbours, {none} not placed.";

        List<string> lines = new();
        foreach (TableMatch m in r.Matches.OrderBy(m => m.Kind).ThenBy(m => m.Source.Name, StringComparer.OrdinalIgnoreCase))
        {
            string mark = m.Kind switch { MatchKind.Content => "●", MatchKind.Inferred => "◐", _ => "○" };
            string detail = m.Kind == MatchKind.Unresolved
                ? "not placed"
                : $"moved {m.Shift:+#;-#;0} bytes, {m.Similarity:P0} of its values the same";
            lines.Add($"{mark}  {m.Source.Name}   -   {detail}");
        }

        this.list.ItemsSource = lines;
        this.results.IsVisible = true;
        this.UpdateUse();
    }

    private void UpdateUse()
    {
        if (this.found == null)
        {
            this.use.IsEnabled = false;
            return;
        }

        int count = this.found.Result.Count(MatchKind.Content) + (this.includeInferred.IsChecked == true ? this.found.Result.Count(MatchKind.Inferred) : 0);
        this.use.IsEnabled = count > 0;
        this.use.Content = count > 0 ? $"Use {count} tables" : "Use these tables";
    }
}
