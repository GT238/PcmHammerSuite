using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PcmSuite.Tuning.Core.Models;

namespace PcmSuite.Tune;

/// <summary>
/// Edits (or creates) the definition of one table: where its values are in the BIN, how they are scaled and where its headings come from.
/// A live preview shows the numbers the definition would give for the open BIN, so a wrong address or size is easy to spot.
/// Applying changes this session's definition only; "Save as my definition" keeps it. Built in code, like the other small pages.
/// </summary>
public sealed class DefinitionEditorView : OverlayPage
{
    private const int PreviewRows = 12;
    private const int PreviewColumns = 12;

    private readonly TuneViewModel tune;
    private readonly DefinitionDraft draft;
    private readonly Button apply = new() { Content = "Apply", IsDefault = true, MinWidth = 90 };
    private readonly StackPanel messages = new() { Spacing = 2 };
    private readonly Grid preview = new() { HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock previewNote = new() { Opacity = 0.7, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private bool building = true;

    public DefinitionEditorView(TuneViewModel tune, DefinitionDraft draft)
    {
        this.tune = tune;
        this.draft = draft;

        StackPanel form = new() { Spacing = 10 };
        form.Children.Add(new TextBlock
        {
            Text = draft.Original == null ? "New table" : "Edit definition  -  " + draft.Original.Name,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        form.Children.Add(Note(draft.Original == null
            ? "Describe where one table lives in the BIN. Nothing is written to the BIN or to the XDF you opened."
            : "Changes apply to this session's definition only. The XDF you opened is not changed; use Save as my definition to keep your version."));
        if (draft.HasSpecialLayout)
        {
            form.Children.Add(Note("This table keeps each row at its own address or uses a stride. Those parts are carried over as they are."));
        }

        form.Children.Add(Heading("The table"));
        form.Children.Add(this.Wrap(
            Field("Name", this.Text(draft.Name, 320, v => draft.Name = v)),
            Field("Category", this.Text(draft.Category, 160, v => draft.Category = v)),
            Field("Sub category (optional)", this.Text(draft.SubCategory, 160, v => draft.SubCategory = v))));
        TextBox description = this.Text(draft.Description, 560, v => draft.Description = v);
        description.AcceptsReturn = true;
        description.TextWrapping = TextWrapping.Wrap;
        description.MinHeight = 56;
        form.Children.Add(Field("Description (optional)", description));

        form.Children.Add(Heading("Where the values are"));
        form.Children.Add(this.Wrap(
            Field("Address (hex)", this.Text(draft.Address, 110, v => draft.Address = v, "0x8360")),
            Field("Rows", this.Text(draft.Rows, 70, v => draft.Rows = v)),
            Field("Columns", this.Text(draft.Columns, 70, v => draft.Columns = v)),
            Field("Value size", this.Bits(draft.ElementBits, v => draft.ElementBits = v)),
            Field("Flag mask (hex, only for one on/off bit)", this.Text(draft.Mask, 110, v => draft.Mask = v))));
        form.Children.Add(this.Wrap(
            this.Check("Signed (can be negative)", draft.IsSigned, v => draft.IsSigned = v),
            this.Check("Little-endian (low byte first)", draft.IsLittleEndian, v => draft.IsLittleEndian = v)));

        form.Children.Add(Heading("What the values mean"));
        form.Children.Add(this.Wrap(
            Field("Units", this.Text(draft.Units, 140, v => draft.Units = v)),
            Field("Decimals", this.Text(draft.Decimals, 70, v => draft.Decimals = v)),
            Field("Scaling equation", this.Text(draft.Equation, 320, v => draft.Equation = v, "X"))));
        form.Children.Add(Note("In the equation, X is the number stored in the BIN. For example X*0.25 shows a stored 1600 as 400, and X/10 shows 255 as 25.5."));

        form.Children.Add(Heading("Column headings (across the top)"));
        form.Children.Add(this.AxisPanel(draft.X));
        form.Children.Add(Heading("Row headings (down the side)"));
        form.Children.Add(this.AxisPanel(draft.Y));

        form.Children.Add(Heading("Check"));
        form.Children.Add(this.messages);
        form.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = this.preview,
        });
        form.Children.Add(this.previewNote);

        Button cancel = new() { Content = "Cancel", MinWidth = 90 };
        cancel.Click += (_, _) => this.RequestClose();
        this.apply.Click += (_, _) => this.OnApply();

        StackPanel buttons = new() { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { this.apply, cancel } };
        Grid layout = new() { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 10 };
        layout.Children.Add(new ScrollViewer { Content = form, Padding = new Thickness(0, 0, 12, 0) });
        Grid.SetRow(buttons, 1);
        layout.Children.Add(buttons);
        this.Content = layout;

        this.building = false;
        this.Refresh();
    }

    // ── Building blocks ──────────────────────────────────────────────

    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontWeight = FontWeight.SemiBold,
        Opacity = 0.8,
        Margin = new Thickness(0, 8, 0, 0),
    };

    private static TextBlock Note(string text) => new() { Text = text, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, FontSize = 12 };

    private static StackPanel Field(string label, Control control) => new()
    {
        Spacing = 2,
        Margin = new Thickness(0, 0, 12, 4),
        Children = { new TextBlock { Text = label, FontSize = 12, Opacity = 0.7 }, control },
    };

    private WrapPanel Wrap(params Control[] items)
    {
        WrapPanel panel = new();
        foreach (Control c in items)
        {
            panel.Children.Add(c);
        }

        return panel;
    }

    private TextBox Text(string value, double width, Action<string> set, string? hint = null)
    {
        TextBox box = new() { Text = value, Width = width, PlaceholderText = hint };
        box.TextChanged += (_, _) =>
        {
            set(box.Text ?? string.Empty);
            this.Refresh();
        };
        return box;
    }

    private CheckBox Check(string label, bool value, Action<bool> set)
    {
        CheckBox box = new() { Content = label, IsChecked = value, Margin = new Thickness(0, 0, 12, 0) };
        box.IsCheckedChanged += (_, _) =>
        {
            set(box.IsChecked == true);
            this.Refresh();
        };
        return box;
    }

    private ComboBox Bits(int value, Action<int> set)
    {
        ComboBox box = new() { ItemsSource = new[] { "8 bit", "16 bit", "32 bit" }, Width = 100 };
        box.SelectedIndex = value switch { 8 => 0, 32 => 2, _ => 1 };
        box.SelectionChanged += (_, _) =>
        {
            set(box.SelectedIndex switch { 0 => 8, 2 => 32, _ => 16 });
            this.Refresh();
        };
        return box;
    }

    private Control AxisPanel(AxisDraft axis)
    {
        ComboBox mode = new()
        {
            ItemsSource = new[] { "Not known (1, 2, 3...)", "Typed headings", "Read from the BIN" },
            SelectedIndex = (int)axis.Mode,
            Width = 220,
        };

        StackPanel typed = new() { Children = { Field("Headings (separated by commas)", this.Text(axis.Labels, 560, v => axis.Labels = v, "400, 800, 1200, 1600")) } };
        StackPanel fromBin = new()
        {
            Children =
            {
                this.Wrap(
                    Field("Address of the first heading (hex)", this.Text(axis.Address, 110, v => axis.Address = v, "0x8300")),
                    Field("Value size", this.Bits(axis.Bits, v => axis.Bits = v)),
                    Field("Equation", this.Text(axis.Equation, 200, v => axis.Equation = v, "X")),
                    Field("Decimals", this.Text(axis.Decimals, 70, v => axis.Decimals = v))),
                this.Wrap(
                    this.Check("Signed", axis.IsSigned, v => axis.IsSigned = v),
                    this.Check("Little-endian", axis.IsLittleEndian, v => axis.IsLittleEndian = v)),
            },
        };

        void Show()
        {
            typed.IsVisible = axis.Mode == AxisMode.Labels;
            fromBin.IsVisible = axis.Mode == AxisMode.FromBin;
        }

        mode.SelectionChanged += (_, _) =>
        {
            axis.Mode = (AxisMode)Math.Max(0, mode.SelectedIndex);
            Show();
            this.Refresh();
        };
        Show();

        return new StackPanel
        {
            Spacing = 4,
            Children = { this.Wrap(Field("Where they come from", mode), Field("Units", this.Text(axis.Units, 140, v => axis.Units = v))), typed, fromBin },
        };
    }

    // ── Checking and previewing ───────────────────────────────────────

    private void Refresh()
    {
        if (this.building)
        {
            return;
        }

        byte[] bin = this.tune.BinData ?? Array.Empty<byte>();
        bool ok = this.draft.TryBuild(bin, this.tune.AllTables, out TableDefinition? table, out List<string> errors, out List<string> warnings);

        this.messages.Children.Clear();
        foreach (string error in errors)
        {
            this.messages.Children.Add(new TextBlock { Text = "• " + error, Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap });
        }

        foreach (string warning in warnings)
        {
            this.messages.Children.Add(new TextBlock { Text = "• " + warning, Foreground = Brushes.Goldenrod, TextWrapping = TextWrapping.Wrap });
        }

        if (ok && warnings.Count == 0)
        {
            this.messages.Children.Add(new TextBlock { Text = "Nothing wrong found.", Opacity = 0.7 });
        }

        this.apply.IsEnabled = ok;
        this.preview.Children.Clear();
        this.preview.RowDefinitions.Clear();
        this.preview.ColumnDefinitions.Clear();
        this.previewNote.Text = string.Empty;

        if (!ok || table == null)
        {
            return;
        }

        ValueEditorViewModel? editor = this.tune.PreviewEditor(table);
        if (editor == null)
        {
            return;
        }

        int rows = Math.Min(editor.Rows, PreviewRows);
        int columns = Math.Min(editor.Columns, PreviewColumns);
        for (int r = 0; r <= rows; r++)
        {
            this.preview.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }

        for (int c = 0; c <= columns; c++)
        {
            this.preview.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        }

        bool columnHeadings = columns > 1 || editor.HasColumnLabels;
        if (columnHeadings)
        {
            for (int c = 0; c < columns; c++)
            {
                this.Place(editor.ColumnLabel(c), 0, c + 1, header: true);
            }
        }

        for (int r = 0; r < rows; r++)
        {
            if (editor.Rows > 1)
            {
                this.Place(editor.RowLabel(r), r + 1, 0, header: true);
            }

            for (int c = 0; c < columns; c++)
            {
                this.Place(editor.CellText(r, c), r + 1, c + 1, header: false);
            }
        }

        string note = $"Preview from the open BIN: {editor.Rows} row(s) x {editor.Columns} column(s)";
        if (editor.Rows > rows || editor.Columns > columns)
        {
            note += $", the first {rows} x {columns} shown";
        }

        this.previewNote.Text = note + ". If these numbers look wrong, the address, size or scaling is not right yet.";
    }

    private void Place(string text, int row, int column, bool header)
    {
        TextBlock cell = new()
        {
            Text = text,
            Margin = new Thickness(0, 0, 14, 2),
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, monospace"),
            FontSize = 12,
            FontWeight = header ? FontWeight.SemiBold : FontWeight.Normal,
            Opacity = header ? 0.75 : 1,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Grid.SetRow(cell, row);
        Grid.SetColumn(cell, column);
        this.preview.Children.Add(cell);
    }

    private void OnApply()
    {
        byte[] bin = this.tune.BinData ?? Array.Empty<byte>();
        if (!this.draft.TryBuild(bin, this.tune.AllTables, out TableDefinition? table, out _, out _) || table == null)
        {
            return;
        }

        this.tune.ApplyDefinition(this.draft.Original, table);
        this.RequestClose();
    }
}
