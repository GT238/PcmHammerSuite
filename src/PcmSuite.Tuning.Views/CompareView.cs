using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using PcmSuite.Tuning.Core.Models;

namespace PcmSuite.Tune;

/// <summary>
/// The compare workbench: pick another BIN, see every table that holds different values (and how many bytes no table
/// accounts for), look at the changed cells, and copy chosen tables from the other BIN into the open one. Copies can be undone.
/// Built in code, like the other small pages.
/// </summary>
public sealed class CompareView : OverlayPage
{
    private const int MaxCellsShown = 400;

    private readonly TuneViewModel tune;
    private readonly ListBox tableList = new() { SelectionMode = SelectionMode.Multiple | SelectionMode.Toggle, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock detail = new() { FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, monospace"), FontSize = 12 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
    private readonly Button copyButton = new() { Content = "Copy selected tables into the open BIN", IsEnabled = false };
    private readonly Button undoButton = new() { Content = "Undo last copy" };
    private readonly Button selectAll = new() { Content = "Select all", IsEnabled = false };

    private byte[]? other;
    private string otherName = string.Empty;
    private List<TableDiff> diffs = new();

    public CompareView(TuneViewModel tune)
    {
        this.tune = tune;

        Button choose = new() { Content = "Choose BIN to compare with..." };
        choose.Click += async (_, _) => await this.OnChoose();
        this.selectAll.Click += (_, _) => this.tableList.SelectAll();
        this.copyButton.Click += async (_, _) => await this.OnCopy();
        this.undoButton.Click += async (_, _) => await this.OnUndo();
        this.tableList.SelectionChanged += (_, _) => this.OnSelection();

        // Long table names are cut with an ellipsis; the tooltip has the whole line.
        this.tableList.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>((text, _) =>
        {
            TextBlock block = new() { Text = text, TextTrimming = TextTrimming.CharacterEllipsis };
            ToolTip.SetTip(block, text);
            return block;
        });
        tune.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TuneViewModel.CanUndoCopyBack))
            {
                this.undoButton.IsVisible = tune.CanUndoCopyBack;
            }
        };
        this.undoButton.IsVisible = tune.CanUndoCopyBack;

        Button close = new() { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => this.RequestClose();

        ScrollViewer detailScroll = new() { Content = this.detail, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        // The list gets the full width (table names are long); the changed cells of the chosen table sit underneath it.
        Grid body = new() { RowDefinitions = new RowDefinitions("3*,2*"), RowSpacing = 10 };
        body.Children.Add(this.tableList);
        Grid.SetRow(detailScroll, 1);
        body.Children.Add(detailScroll);

        Grid layout = new() { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto,Auto"), RowSpacing = 8 };
        this.Place(layout, new TextBlock { Text = "Compare with another BIN  -  " + tune.BinName, FontSize = 16, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis }, 0);
        this.Place(layout, new WrapPanel { Children = { this.Spaced(choose), this.Spaced(this.selectAll), this.Spaced(this.copyButton), this.Spaced(this.undoButton) } }, 1);
        this.Place(layout, this.summary, 2);
        this.Place(layout, body, 3);
        this.Place(layout, this.status, 4);
        this.Place(layout, close, 5);
        this.Content = layout;

        this.summary.Text = "Choose a BIN of the same size, such as the stock file or a friend's tune. Tables whose values differ are listed here; " +
                            "pick some and copy them into the open BIN. Nothing is saved until you use Save As.";
    }

    private Control Spaced(Control c)
    {
        c.Margin = new Thickness(0, 0, 8, 6);
        return c;
    }

    private void Place(Grid grid, Control child, int row)
    {
        Grid.SetRow(child, row);
        grid.Children.Add(child);
    }

    private async Task OnChoose()
    {
        if (TopLevel.GetTopLevel(this) is not { } top || this.tune.BinData == null)
        {
            return;
        }

        IReadOnlyList<IStorageFile> files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose the BIN to compare with",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("BIN files") { Patterns = new[] { "*.bin", "*.BIN" } } },
        });

        string? path = files.Count == 1 ? files[0].TryGetLocalPath() : null;
        if (path == null)
        {
            return;
        }

        try
        {
            byte[] bytes = await Task.Run(() => File.ReadAllBytes(path));
            if (bytes.Length != this.tune.BinData.Length)
            {
                this.status.Text = $"Cannot compare: {Path.GetFileName(path)} is {bytes.Length:N0} bytes, the open BIN is {this.tune.BinData.Length:N0}.";
                return;
            }

            this.other = bytes;
            this.otherName = Path.GetFileName(path);
            await this.Refresh();
        }
        catch (Exception ex)
        {
            this.status.Text = "Could not read the file: " + ex.Message;
        }
    }

    /// <summary>Compare again (after choosing a file, a copy or an undo), keeping the page's picture of the two BINs current.</summary>
    private async Task Refresh()
    {
        byte[]? open = this.tune.BinData;
        byte[]? bytes = this.other;
        if (open == null || bytes == null)
        {
            return;
        }

        List<TableDefinition> tables = this.tune.AllTables.ToList();
        BinComparison result = await Task.Run(() => BinCompare.Compare(tables, open, bytes));
        this.diffs = result.Tables.ToList();

        this.tableList.ItemsSource = this.diffs.Select(d => $"{d.Table.Name}   ({d.CellsChanged} of {d.CellsTotal} cell{(d.CellsTotal == 1 ? string.Empty : "s")})").ToList();
        this.selectAll.IsEnabled = this.diffs.Count > 0;
        this.detail.Text = string.Empty;
        this.OnSelection();

        string text = result.DifferingBytes == 0
            ? $"The open BIN is identical to {this.otherName}."
            : $"{this.diffs.Count} table{(this.diffs.Count == 1 ? string.Empty : "s")} differ from {this.otherName}. " +
              $"{result.UnmappedBytes:N0} of {result.DifferingBytes:N0} differing bytes are outside every table in the XDF (code, checksums, OS id) and are not listed; " +
              "the hex view shows them.";
        if (this.tune.IsBinOnly)
        {
            text = "Without an XDF there are no tables to list. Add the XDF, or use the hex view to see the differing bytes.";
        }

        this.summary.Text = text;
    }

    private IReadOnlyList<TableDiff> Selected() =>
        this.tableList.Selection.SelectedIndexes.Where(i => i >= 0 && i < this.diffs.Count).Select(i => this.diffs[i]).ToList();

    private void OnSelection()
    {
        IReadOnlyList<TableDiff> picked = this.Selected();
        this.copyButton.IsEnabled = picked.Count > 0 && this.other != null;
        this.copyButton.Content = picked.Count > 1 ? $"Copy {picked.Count} tables into the open BIN" : "Copy selected table into the open BIN";

        byte[]? open = this.tune.BinData;
        if (picked.Count == 0 || open == null || this.other == null)
        {
            this.detail.Text = string.Empty;
            return;
        }

        TableDiff last = picked[^1];
        this.detail.Text = this.Describe(last.Table, open, this.other);
    }

    private string Describe(TableDefinition table, byte[] open, byte[] bytes)
    {
        System.Text.StringBuilder text = new();
        text.AppendLine($"{table.Name}");
        text.AppendLine($"open BIN  ->  {this.otherName}");
        int shown = 0;
        int rows = Math.Max(1, table.Rows);
        int columns = Math.Max(1, table.Columns);
        for (int r = 0; r < rows && shown < MaxCellsShown; r++)
        {
            for (int c = 0; c < columns && shown < MaxCellsShown; c++)
            {
                if (table.GetRawValue(open, r, c) == table.GetRawValue(bytes, r, c))
                {
                    continue;
                }

                string mine = table.FormatValue(table.GetScaledValue(open, r, c));
                string theirs = table.FormatValue(table.GetScaledValue(bytes, r, c));
                string where = rows > 1 && columns > 1 ? $"row {r + 1}, col {c + 1}" : $"cell {r * columns + c + 1}";
                text.AppendLine($"{where,-16} {mine,10}  ->  {theirs,-10} {table.ZUnits}");
                shown++;
            }
        }

        if (shown == MaxCellsShown)
        {
            text.AppendLine($"(the first {MaxCellsShown} changed cells)");
        }

        return text.ToString();
    }

    private async Task OnCopy()
    {
        IReadOnlyList<TableDiff> picked = this.Selected();
        if (picked.Count == 0 || this.other == null || this.tune.BinData == null)
        {
            return;
        }

        string names = picked.Count <= 3 ? string.Join(", ", picked.Select(p => p.Table.Name)) : $"{picked.Count} tables";
        if (this.Confirm != null
            && !await this.Confirm("Copy from " + this.otherName + "?",
                $"This overwrites {names} in the open BIN with the values from {this.otherName}. You can undo it, and nothing is saved until you use Save As.",
                "Copy"))
        {
            return;
        }

        await this.tune.CopyTablesFromAsync(this.other, picked.Select(p => p.Table).ToList(), this.otherName);
        await this.Refresh();
        this.status.Text = this.tune.Status;
    }

    private async Task OnUndo()
    {
        await this.tune.UndoCopyBackAsync();
        await this.Refresh();
        this.status.Text = this.tune.Status;
    }
}
