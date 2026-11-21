using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace PcmSuite.Tune;

/// <summary>
/// Hex view of the open BIN with go-to-address, search, the table that covers the selected byte, and a list of every
/// difference from another BIN. Built in code, like the other small pages.
/// </summary>
public sealed class HexView : OverlayPage
{
    private const int MaxListed = 2000;

    private readonly TuneViewModel tune;
    private readonly HexControl hex = new() { MinHeight = 200 };
    private readonly ScrollBar scroll = new() { Orientation = Orientation.Vertical, AllowAutoHide = false, Visibility = ScrollBarVisibility.Visible };
    private readonly TextBox gotoBox = new() { PlaceholderText = "address (hex)", Width = 130 };
    private readonly TextBox findBox = new() { PlaceholderText = "FF 8A  or  \"12212156\"", Width = 190 };
    private readonly TextBlock info = new() { TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, monospace"), FontSize = 12 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.75 };
    private readonly ListBox diffList = new();
    // A grid, not a stack, so the list gets only the height that is left and scrolls inside it.
    private readonly Grid diffPanel = new() { RowDefinitions = new RowDefinitions("Auto,Auto,*"), RowSpacing = 6, Width = 210, Margin = new Thickness(10, 0, 0, 0), IsVisible = false };
    private readonly TextBlock diffTitle = new() { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly Button compareButton = new() { Content = "Compare with BIN..." };
    private readonly Button stopCompare = new() { Content = "Stop comparing", IsVisible = false };

    private List<DiffRange> ranges = new();
    private byte[]? reference;
    private bool syncing;

    public HexView(TuneViewModel tune)
    {
        this.tune = tune;
        byte[] data = tune.BinData ?? Array.Empty<byte>();
        this.hex.Data = data;

        this.hex.ViewChanged += (_, _) => this.OnViewChanged();
        this.scroll.ValueChanged += (_, e) =>
        {
            if (!this.syncing)
            {
                this.hex.TopRow = (int)e.NewValue;
            }
        };

        Button go = new() { Content = "Go" };
        go.Click += (_, _) => this.OnGoTo();
        this.gotoBox.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                this.OnGoTo();
            }
        };

        Button findNext = new() { Content = "Next" };
        Button findPrevious = new() { Content = "Previous" };
        findNext.Click += (_, _) => this.OnFind(true);
        findPrevious.Click += (_, _) => this.OnFind(false);
        this.findBox.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                this.OnFind(true);
            }
        };

        Button openTable = new() { Content = "Open table" };
        openTable.Click += (_, _) => this.ShowOpenTable();
        ToolTip.SetTip(openTable, "Shade the table that is open in the editor and scroll to it");

        this.compareButton.Click += async (_, _) => await this.OnChooseReference();
        this.stopCompare.Click += (_, _) => this.SetReference(null, string.Empty);

        Button close = new() { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => this.RequestClose();

        Button previousDiff = new() { Content = "Previous" };
        Button nextDiff = new() { Content = "Next" };
        previousDiff.Click += (_, _) => this.StepDiff(false);
        nextDiff.Click += (_, _) => this.StepDiff(true);
        this.diffList.SelectionChanged += (_, _) =>
        {
            if (!this.syncing && this.diffList.SelectedIndex is >= 0 and var i && i < this.ranges.Count)
            {
                this.hex.Select(this.ranges[i].Start, true);
                this.hex.Focus();
            }
        };
        StackPanel diffButtons = new() { Orientation = Orientation.Horizontal, Spacing = 6, Children = { previousDiff, nextDiff } };
        Grid.SetRow(diffButtons, 1);
        Grid.SetRow(this.diffList, 2);
        this.diffPanel.Children.Add(this.diffTitle);
        this.diffPanel.Children.Add(diffButtons);
        this.diffPanel.Children.Add(this.diffList);

        Grid body = new() { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*") };
        body.Children.Add(this.hex);
        Grid.SetColumn(this.scroll, 1);
        body.Children.Add(this.scroll);
        Grid.SetColumn(this.diffPanel, 2);
        body.Children.Add(this.diffPanel);

        Grid layout = new() { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto,Auto,Auto"), RowSpacing = 8 };
        layout.Children.Add(new TextBlock { Text = "Hex view  -  " + tune.BinName, FontSize = 16, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        this.Place(layout, new WrapPanel { Children = { this.Spaced(this.gotoBox), this.Spaced(go), this.Spaced(this.findBox), this.Spaced(findPrevious), this.Spaced(findNext) } }, 1);
        this.Place(layout, new WrapPanel { Children = { this.Spaced(openTable), this.Spaced(this.compareButton), this.Spaced(this.stopCompare) } }, 2);
        this.Place(layout, body, 3);
        this.Place(layout, this.info, 4);
        this.Place(layout, this.status, 5);
        this.Place(layout, close, 6);
        this.Content = layout;

        this.AttachedToVisualTree += (_, _) =>
        {
            if (tune.ReferenceData != null)
            {
                this.SetReference(tune.ReferenceData, tune.ReferenceName);
            }

            this.ShowOpenTable();
            this.OnViewChanged();
        };

        this.status.Text = "Click a byte to see its value and the table it belongs to. Blue = the open table, red = differs from the compared BIN.";
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

    private void OnViewChanged()
    {
        this.syncing = true;
        this.scroll.Maximum = this.hex.MaxTopRow;
        this.scroll.ViewportSize = this.hex.VisibleRows;
        this.scroll.LargeChange = this.hex.VisibleRows;
        this.scroll.Value = this.hex.TopRow;
        this.syncing = false;

        byte[] data = this.hex.Data;
        if (data.Length == 0)
        {
            this.info.Text = "No BIN is open.";
            return;
        }

        int at = this.hex.SelectedIndex;
        string text = HexTools.Describe(data, at);
        if (this.tune.TableNameAt(at) is { } table)
        {
            text += "\nTable: " + table;
        }

        if (this.reference != null && at < this.reference.Length && this.reference[at] != data[at])
        {
            text += $"\nCompared BIN has 0x{this.reference[at]:X2} here";
        }

        this.info.Text = text;
    }

    private void OnGoTo()
    {
        if (!HexTools.TryParseAddress(this.gotoBox.Text, out int address) || address >= this.hex.Data.Length)
        {
            this.status.Text = $"Type an address in hex between 0 and {Math.Max(0, this.hex.Data.Length - 1):X}.";
            return;
        }

        this.hex.Select(address, true);
        this.hex.Focus();
    }

    private void OnFind(bool forward)
    {
        if (!HexTools.TryParsePattern(this.findBox.Text, out byte[] pattern))
        {
            this.status.Text = "Type hex bytes (FF 8A 00) or text in quotes (\"12212156\").";
            return;
        }

        int from = this.hex.SelectedIndex + (forward ? 1 : -1);
        int found = HexTools.Find(this.hex.Data, pattern, from, forward);
        if (found < 0)
        {
            this.status.Text = "Not found.";
            return;
        }

        this.hex.Select(found, true);
        this.status.Text = $"Found at ${found:X6}.";
    }

    private void ShowOpenTable()
    {
        if (this.tune.Editor?.Table is not { } table)
        {
            this.hex.SetHighlight(0, 0);
            return;
        }

        (int start, int end) = TuneViewModel.Extent(table);
        this.hex.SetHighlight(start, end);
        this.hex.Select(start, true);
        this.status.Text = $"{table.Name}: ${start:X6} to ${end - 1:X6} ({end - start} bytes).";
    }

    private async Task OnChooseReference()
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
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
            byte[] other = await Task.Run(() => File.ReadAllBytes(path));
            if (other.Length != this.hex.Data.Length)
            {
                this.status.Text = $"Cannot compare: {Path.GetFileName(path)} is {other.Length:N0} bytes, this BIN is {this.hex.Data.Length:N0}.";
                return;
            }

            this.SetReference(other, Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            this.status.Text = "Could not read the file: " + ex.Message;
        }
    }

    private void SetReference(byte[]? other, string name)
    {
        this.reference = other;
        this.hex.Reference = other;
        this.diffPanel.IsVisible = other != null;
        this.stopCompare.IsVisible = other != null;
        this.compareButton.IsVisible = other == null;

        this.ranges = other == null ? new List<DiffRange>() : HexTools.Differences(this.hex.Data, other);
        int total = this.ranges.Count;
        long bytes = this.ranges.Sum(r => (long)r.Length);

        this.syncing = true;
        this.diffList.ItemsSource = this.ranges.Take(MaxListed).Select(this.Describe).ToList();
        this.diffList.SelectedIndex = -1;
        this.syncing = false;

        if (other != null)
        {
            this.diffTitle.Text = total == 0
                ? $"Identical to {name}."
                : $"{total:N0} difference{(total == 1 ? string.Empty : "s")} from {name} ({bytes:N0} bytes)" + (total > MaxListed ? $"; the first {MaxListed:N0} are listed." : ".");
            this.status.Text = total == 0 ? $"No byte differs from {name}." : this.status.Text;
        }

        this.OnViewChanged();
    }

    private string Describe(DiffRange r)
    {
        string where = r.Length == 1 ? $"${r.Start:X6}" : $"${r.Start:X6}-${r.End - 1:X6}";
        string table = this.tune.TableNameAt(r.Start) is { } name ? "  " + name : string.Empty;
        return $"{where}  ({r.Length}){table}";
    }

    private void StepDiff(bool forward)
    {
        if (this.ranges.Count == 0)
        {
            return;
        }

        int at = this.hex.SelectedIndex;
        int index = forward
            ? this.ranges.FindIndex(r => r.Start > at)
            : this.ranges.FindLastIndex(r => r.Start < at);
        if (index < 0)
        {
            index = forward ? 0 : this.ranges.Count - 1;
        }

        this.hex.Select(this.ranges[index].Start, true);
        if (index < MaxListed)
        {
            this.syncing = true;
            this.diffList.SelectedIndex = index;
            this.diffList.ScrollIntoView(index);
            this.syncing = false;
        }
    }
}
