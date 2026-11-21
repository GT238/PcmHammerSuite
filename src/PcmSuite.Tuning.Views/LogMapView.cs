using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace PcmSuite.Tune;

/// <summary>
/// The open table drawn as a map of a logged drive: how many samples fell in each cell, or the average, lowest or highest of a channel.
/// Built in code, like the other small pages. Read only: it never changes the BIN.
/// </summary>
public sealed class LogMapView : OverlayPage
{
    private static readonly string[] Figures = { "Average", "Lowest", "Highest" };

    private readonly LogMapViewModel model;
    private readonly Func<string?>? logFolder;
    private readonly TextBlock logName = new() { FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox rpmBox = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox loadBox = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox valueBox = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox figureBox = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = Figures, SelectedIndex = 0 };
    private readonly CheckBox rpmColumns = new() { Content = "Engine speed runs along the table's columns (the top row)" };
    private readonly CheckBox showTable = new() { Content = "Show the table's own values too" };
    private readonly Border mapHost = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
    private bool loading;

    public LogMapView(LogMapViewModel model, Func<string?>? logFolder)
    {
        this.model = model;
        this.logFolder = logFolder;
        this.rpmColumns.IsChecked = model.RpmOnColumns;

        Button open = new() { Content = "Open log..." };
        Button newest = new() { Content = "Newest Logger log" };
        open.Click += async (_, _) => await this.OnOpenLog();
        newest.Click += async (_, _) => await this.OnNewestLog();
        ToolTip.SetTip(newest, "The most recent PcmLog file in the Logger's log folder");

        this.rpmBox.SelectionChanged += (_, _) => this.OnChoice();
        this.loadBox.SelectionChanged += (_, _) => this.OnChoice();
        this.valueBox.SelectionChanged += (_, _) => this.OnChoice();
        this.figureBox.SelectionChanged += (_, _) => this.OnChoice();
        this.rpmColumns.IsCheckedChanged += (_, _) => this.OnChoice();
        this.showTable.IsCheckedChanged += (_, _) => this.Redraw();

        Button close = new() { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => this.RequestClose();

        Grid choices = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"), ColumnSpacing = 10, RowSpacing = 6 };
        AddRow(choices, 0, "Engine speed", this.rpmBox);
        AddRow(choices, 1, "Load (MAP)", this.loadBox);
        AddRow(choices, 2, "Show", this.valueBox);
        AddRow(choices, 3, "Figure", this.figureBox);

        StackPanel top = new()
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Where the drive went", FontSize = 16, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = "Table: " + model.TableName, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock
                {
                    Text = "Each logged sample is placed in the cell the table's own axes put it in. By default a cell shows how many samples landed there; " +
                           "choose a channel to see its average, lowest or highest instead. Nothing here changes the BIN.",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.7,
                },
                new WrapPanel { Children = { Spaced(open), Spaced(newest), this.logName } },
                choices,
                this.rpmColumns,
                this.showTable,
            },
        };

        ScrollViewer scroller = new() { Content = this.mapHost, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Margin = new Thickness(0, 8, 0, 0) };
        Grid layout = new() { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), RowSpacing = 6 };
        layout.Children.Add(top);
        Grid.SetRow(scroller, 1);
        layout.Children.Add(scroller);
        Grid.SetRow(this.status, 2);
        layout.Children.Add(this.status);
        Grid.SetRow(close, 3);
        layout.Children.Add(close);
        this.Content = layout;

        this.status.Text = model.Status;
        this.Redraw();
    }

    private static Control Spaced(Control c)
    {
        c.Margin = new Thickness(0, 0, 8, 6);
        return c;
    }

    private static void AddRow(Grid grid, int row, string label, Control box)
    {
        TextBlock text = new() { Text = label, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(text, row);
        Grid.SetRow(box, row);
        Grid.SetColumn(box, 1);
        grid.Children.Add(text);
        grid.Children.Add(box);
    }

    private async Task OnOpenLog()
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        IStorageFolder? start = null;
        string? folder = this.logFolder?.Invoke();
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
        {
            start = await top.StorageProvider.TryGetFolderFromPathAsync(folder);
        }

        IReadOnlyList<IStorageFile> files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a log file",
            AllowMultiple = false,
            SuggestedStartLocation = start,
            FileTypeFilter = new[] { new FilePickerFileType("Log files") { Patterns = new[] { "*.csv" } } },
        });

        string? path = files.Count == 1 ? files[0].TryGetLocalPath() : null;
        if (path != null)
        {
            await this.Load(path);
        }
    }

    private async Task OnNewestLog()
    {
        string? folder = this.logFolder?.Invoke();
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            this.status.Text = "The Logger's log folder does not exist yet. Record a log in the Logger tab first.";
            return;
        }

        string? newest = new DirectoryInfo(folder).GetFiles("*.csv")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => f.FullName)
            .FirstOrDefault();
        if (newest == null)
        {
            this.status.Text = "No log files in " + folder;
            return;
        }

        await this.Load(newest);
    }

    private async Task Load(string path)
    {
        if (!await this.model.LoadAsync(path))
        {
            this.status.Text = this.model.Status;
            return;
        }

        this.loading = true;
        this.logName.Text = this.model.LogName;
        List<string> headings = this.model.Headings.ToList();
        this.rpmBox.ItemsSource = headings;
        this.loadBox.ItemsSource = headings;
        this.valueBox.ItemsSource = new[] { "(just count samples)" }.Concat(headings).ToList();
        this.rpmBox.SelectedIndex = this.model.RpmIndex;
        this.loadBox.SelectedIndex = this.model.LoadIndex;
        this.valueBox.SelectedIndex = 0;
        this.loading = false;

        this.OnChoice();
    }

    private void OnChoice()
    {
        if (this.loading)
        {
            return;
        }

        this.model.RpmIndex = this.rpmBox.SelectedIndex;
        this.model.LoadIndex = this.loadBox.SelectedIndex;
        this.model.ValueIndex = this.valueBox.SelectedIndex - 1;
        this.model.RpmOnColumns = this.rpmColumns.IsChecked == true;
        this.model.Stat = this.figureBox.SelectedIndex switch { 1 => CellStat.Minimum, 2 => CellStat.Maximum, _ => CellStat.Average };
        this.figureBox.IsEnabled = this.model.ValueIndex >= 0;

        this.model.Calculate();
        this.status.Text = this.model.Status;
        this.Redraw();
    }

    private void Redraw()
    {
        ValueEditorViewModel editor = this.model.Editor;
        int rows = editor.Rows;
        int cols = editor.Columns;
        bool both = this.showTable.IsChecked == true;

        Grid grid = new() { ColumnSpacing = 1, RowSpacing = 1 };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (int c = 0; c < cols; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)) { MinWidth = 40 });
        }

        for (int r = 0; r <= rows; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }

        for (int c = 0; c < cols; c++)
        {
            Place(grid, Header(editor.ColumnLabel(c)), 0, c + 1);
        }

        for (int r = 0; r < rows; r++)
        {
            Place(grid, Header(editor.RowLabel(r)), r + 1, 0);
            for (int c = 0; c < cols; c++)
            {
                Place(grid, this.Cell(r, c, both), r + 1, c + 1);
            }
        }

        this.mapHost.Child = grid;
    }

    private static TextBlock Header(string text) => new()
    {
        Text = text,
        FontSize = 11,
        FontWeight = FontWeight.SemiBold,
        Opacity = 0.75,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(4, 2),
    };

    private static void Place(Grid grid, Control child, int row, int column)
    {
        Grid.SetRow(child, row);
        Grid.SetColumn(child, column);
        grid.Children.Add(child);
    }

    private Control Cell(int row, int column, bool both)
    {
        double fraction = this.model.Fraction(row, column);
        IBrush fill = double.IsNaN(fraction)
            ? new SolidColorBrush(Color.FromArgb(18, 128, 128, 128))
            : this.model.ValueIndex < 0 || this.model.Stat == CellStat.Count
                ? new SolidColorBrush(Color.FromArgb((byte)(40 + 190 * fraction), 59, 130, 246))      // more samples, stronger blue
                : new SolidColorBrush(Lerp(Color.FromArgb(200, 59, 130, 246), Color.FromArgb(200, 244, 67, 54), fraction));   // low blue to high red

        StackPanel text = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        text.Children.Add(new TextBlock { Text = this.model.CellText(row, column), FontSize = 12, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
        if (both)
        {
            text.Children.Add(new TextBlock { Text = this.model.Editor.CellText(row, column), FontSize = 10, Opacity = 0.6, HorizontalAlignment = HorizontalAlignment.Center });
        }

        Border cell = new() { Background = fill, MinHeight = both ? 40 : 28, Child = text };
        ToolTip.SetTip(cell, this.model.CellTip(row, column));
        return cell;
    }

    private static Color Lerp(Color a, Color b, double t) => Color.FromArgb(
        (byte)(a.A + (b.A - a.A) * t),
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));
}
