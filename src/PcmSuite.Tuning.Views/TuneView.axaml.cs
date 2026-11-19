using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace PcmSuite.Tune;

public partial class TuneView : UserControl
{
    private TuneViewModel? attached;
    private TextBox[,]? cells;

    public TuneView()
    {
        InitializeComponent();
        this.DataContextChanged += (_, _) => this.Attach();

        // Menu items close the slide-out menu once used.
        foreach (Button button in this.MenuPanel.Children.OfType<Button>())
        {
            button.Click += (_, _) => this.MenuToggle.IsChecked = false;
        }

        this.SizeChanged += (_, e) => this.ApplyLayout(e.NewSize.Width);
        this.EditorScroll.ScrollChanged += (_, _) => this.UpdateFrozenHeaders();    }

    private const double WideWidth = 900;

    private bool wide = true;
    private int page; // narrow layout: 0 parameters, 1 editor, 2 details

    private void OnNav(object? sender, RoutedEventArgs e)
    {
        this.page = this.NavEditor.IsChecked == true ? 1 : this.NavDetails.IsChecked == true ? 2 : 0;
        this.ApplyLayout(this.Bounds.Width);
    }

    /// <summary>Side by side on a wide window, one pane at a time on a narrow one.</summary>
    private void ApplyLayout(double width)
    {
        this.wide = width >= WideWidth || width <= 0;
        this.NavBar.IsVisible = !this.wide;

        if (this.wide)
        {
            this.Body.ColumnDefinitions[0].Width = new GridLength(360);
            this.Body.ColumnDefinitions[1].Width = new GridLength(8);
            this.Body.RowDefinitions[1].Height = GridLength.Auto;
            Grid.SetColumn(this.TreePane, 0);
            Grid.SetRowSpan(this.TreePane, 2);
            Grid.SetColumnSpan(this.TreePane, 1);
            Grid.SetColumn(this.EditorPane, 2);
            Grid.SetRow(this.EditorPane, 0);
            Grid.SetColumnSpan(this.EditorPane, 1);
            Grid.SetColumn(this.DetailsPane, 2);
            Grid.SetRow(this.DetailsPane, 1);
            Grid.SetColumnSpan(this.DetailsPane, 1);
            this.DetailsPane.Margin = new Thickness(0, 8, 0, 0);
            this.TreePane.IsVisible = true;
            this.EditorPane.IsVisible = true;
            this.DetailsPane.IsVisible = true;
            return;
        }

        this.Body.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        this.Body.ColumnDefinitions[1].Width = new GridLength(0);
        this.Body.RowDefinitions[1].Height = new GridLength(0);
        foreach (Border pane in new[] { this.TreePane, this.EditorPane, this.DetailsPane })
        {
            Grid.SetColumn(pane, 0);
            Grid.SetRow(pane, 0);
            Grid.SetRowSpan(pane, 1);
            Grid.SetColumnSpan(pane, 3);
        }

        this.DetailsPane.Margin = new Thickness(0);
        this.TreePane.IsVisible = this.page == 0;
        this.EditorPane.IsVisible = this.page == 1;
        this.DetailsPane.IsVisible = this.page == 2;
    }

    private void ShowPage(int target)
    {
        this.page = target;
        this.NavParams.IsChecked = target == 0;
        this.NavEditor.IsChecked = target == 1;
        this.NavDetails.IsChecked = target == 2;
        this.ApplyLayout(this.Bounds.Width);
    }

    private TuneViewModel? ViewModel => this.DataContext as TuneViewModel;

    private void Attach()
    {
        if (this.attached != null)
        {
            this.attached.PropertyChanged -= this.OnViewModelChanged;
        }

        this.attached = this.ViewModel;
        if (this.attached != null)
        {
            this.attached.PropertyChanged += this.OnViewModelChanged;
        }

        this.BuildEditor();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TuneViewModel.Editor) or nameof(TuneViewModel.Patch))
        {
            if (e.PropertyName == nameof(TuneViewModel.Editor))
            {
                this.BuildEditor();
            }

            // Narrow window: picking a table or patch in the list takes you to its editor.
            if (!this.wide && (this.attached?.Editor != null || this.attached?.Patch != null) && this.page == 0)
            {
                this.ShowPage(1);
            }
        }
    }

    // ---- The value grid, built in code so any table size works ----

    private void BuildEditor()
    {
        this.cells = null;
        this.columnHeaders.Clear();
        this.rowHeaders.Clear();
        this.GridHost.Content = null;

        ValueEditorViewModel? editor = this.ViewModel?.Editor;
        this.Surface.Editor = editor;
        if (editor == null)
        {
            return;
        }

        if (editor.IsFlag)
        {
            CheckBox box = new() { Content = "Enabled", IsChecked = editor.GetFlag() };
            box.IsCheckedChanged += (_, _) => editor.SetFlag(box.IsChecked == true);
            this.GridHost.Content = box;
            return;
        }

        Grid grid = new();
        int rowOffset = editor.HasColumnLabels ? 1 : 0;
        int colOffset = editor.HasRowLabels ? 1 : 0;

        for (int i = 0; i < rowOffset + editor.Rows; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }

        for (int i = 0; i < colOffset + editor.Columns + 1; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        }

        if (editor.HasColumnLabels)
        {
            for (int c = 0; c < editor.Columns; c++)
            {
                TextBlock header = this.FrozenHeader(editor.ColumnLabel(c), 0, colOffset + c);
                this.columnHeaders.Add(header);
                grid.Children.Add(header);
            }
        }

        if (editor.HasRowLabels)
        {
            for (int r = 0; r < editor.Rows; r++)
            {
                TextBlock header = this.FrozenHeader(editor.RowLabel(r), rowOffset + r, 0);
                this.rowHeaders.Add(header);
                grid.Children.Add(header);
            }
        }

        TextBox[,] boxes = new TextBox[editor.Rows, editor.Columns];
        for (int r = 0; r < editor.Rows; r++)
        {
            for (int c = 0; c < editor.Columns; c++)
            {
                int row = r;
                int col = c;
                TextBox box = new()
                {
                    Text = editor.CellText(r, c),
                    MinWidth = 64,
                    TextAlignment = TextAlignment.Center,
                    Padding = new Thickness(4, 3),
                    Margin = new Thickness(1),
                    FontFamily = new FontFamily("Consolas, Menlo, DejaVu Sans Mono, monospace"),
                };
                box.LostFocus += (_, _) => this.CommitCell(editor, boxes, row, col);
                box.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Enter)
                    {
                        this.CommitCell(editor, boxes, row, col);
                        e.Handled = true;
                    }
                    else if (e.Key == Key.Escape)
                    {
                        boxes[row, col].Text = editor.CellText(row, col);
                        e.Handled = true;
                    }
                };

                boxes[r, c] = box;
                Grid.SetRow(box, rowOffset + r);
                Grid.SetColumn(box, colOffset + c);
                grid.Children.Add(box);
            }
        }

        if (editor.IsSingleValue && !string.IsNullOrEmpty(editor.Units))
        {
            TextBlock units = new()
            {
                Text = editor.Units,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            };
            Grid.SetRow(units, rowOffset);
            Grid.SetColumn(units, colOffset + 1);
            grid.Children.Add(units);
        }

        this.cells = boxes;
        Recolor(editor, boxes);
        this.GridHost.Content = grid;
        this.UpdateFrozenHeaders();
    }

    // Axis labels stay in view while the table scrolls: they are shifted by the scroll offset.
    private readonly List<TextBlock> columnHeaders = new();
    private readonly List<TextBlock> rowHeaders = new();

    private TextBlock FrozenHeader(string text, int row, int column)
    {
        TextBlock block = new()
        {
            Text = text,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(1),
            Padding = new Thickness(5, 3),
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ZIndex = 10,
            Background = this.PaneBackground(),
        };
        Grid.SetRow(block, row);
        Grid.SetColumn(block, column);
        return block;
    }

    /// <summary>Opaque, so cells scrolling underneath a frozen label do not show through it.</summary>
    private IBrush PaneBackground()
    {
        if (this.TryFindResource("SolidBackgroundFillColorBaseBrush", this.ActualThemeVariant, out object? found)
            && found is IBrush brush)
        {
            return brush;
        }

        return this.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light
            ? new SolidColorBrush(Color.FromRgb(243, 243, 243))
            : new SolidColorBrush(Color.FromRgb(32, 32, 32));
    }

    private void UpdateFrozenHeaders()
    {
        Vector offset = this.EditorScroll.Offset;
        foreach (TextBlock header in this.columnHeaders)
        {
            header.RenderTransform = new TranslateTransform(0, offset.Y);
        }

        foreach (TextBlock header in this.rowHeaders)
        {
            header.RenderTransform = new TranslateTransform(offset.X, 0);
        }
    }

    private void CommitCell(ValueEditorViewModel editor, TextBox[,] boxes, int row, int col)
    {
        string typed = boxes[row, col].Text ?? string.Empty;
        if (typed == editor.CellText(row, col))
        {
            return;
        }

        editor.TryCommit(row, col, typed, out string shown);
        boxes[row, col].Text = shown;
        Recolor(editor, boxes);
    }

    /// <summary>Tint each cell from blue (lowest) to red (highest) across the table.</summary>
    private static void Recolor(ValueEditorViewModel editor, TextBox[,] boxes)
    {
        // Cells that differ from the reference BIN get an orange frame and show the reference value.
        for (int r = 0; r < editor.Rows; r++)
        {
            for (int c = 0; c < editor.Columns; c++)
            {
                bool changed = editor.IsChanged(r, c);
                boxes[r, c].BorderBrush = changed ? Brushes.Orange : null;
                boxes[r, c].BorderThickness = changed ? new Thickness(2) : new Thickness(1);
                ToolTip.SetTip(boxes[r, c], changed ? "Reference: " + editor.ReferenceText(r, c) : null);
            }
        }

        if (editor.IsSingleValue)
        {
            return;
        }

        (double min, double max) = editor.Range();
        double span = max - min;
        for (int r = 0; r < editor.Rows; r++)
        {
            for (int c = 0; c < editor.Columns; c++)
            {
                double t = span <= 0 ? 0.5 : (editor.Scaled(r, c) - min) / span;
                boxes[r, c].Background = new SolidColorBrush(Heat(Math.Clamp(t, 0, 1)));
            }
        }
    }

    private static Color Heat(double t)
    {
        // Hue 240 (blue) down to 0 (red), muted so text stays readable on dark and light themes.
        double hue = (1 - t) * 240;
        const double saturation = 0.55;
        const double value = 0.60;
        double chroma = value * saturation;
        double x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        double m = value - chroma;
        (double r, double g, double b) = hue switch
        {
            < 60 => (chroma, x, 0.0),
            < 120 => (x, chroma, 0.0),
            < 180 => (0.0, chroma, x),
            < 240 => (0.0, x, chroma),
            _ => (x, 0.0, chroma),
        };

        return Color.FromArgb(170, (byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    // ---- Buttons ----

    private async void OnOpenBin(object? sender, RoutedEventArgs e)
    {
        if (!await this.ConfirmDiscardAsync())
        {
            return;
        }

        string? path = await this.PickFileAsync("Open BIN file", "BIN files", "*.bin", "*.BIN");
        if (path != null && this.ViewModel != null)
        {
            await this.ViewModel.OpenBinAsync(path);
        }
    }

    private async void OnOpenXdf(object? sender, RoutedEventArgs e)
    {
        // Loading an XDF re-reads the BIN, so unsaved edits would be lost.
        if (!await this.ConfirmDiscardAsync())
        {
            return;
        }

        string? path = await this.PickFileAsync("Select XDF definition file", "XDF files", "*.xdf", "*.XDF");
        if (path != null && this.ViewModel != null)
        {
            await this.ViewModel.OpenXdfAsync(path);
        }
    }

    private async void OnAutoXdf(object? sender, RoutedEventArgs e)
    {
        if (this.ViewModel != null && await this.ConfirmDiscardAsync())
        {
            await this.ViewModel.AutoDetectXdfAsync();
        }
    }

    private async void OnCompare(object? sender, RoutedEventArgs e)
    {
        string? path = await this.PickFileAsync("Pick the reference BIN (for example the stock file)", "BIN files", "*.bin", "*.BIN");
        if (path != null && this.ViewModel != null)
        {
            await this.ViewModel.CompareAsync(path);
        }
    }

    private void OnSendToFlash(object? sender, RoutedEventArgs e) => this.ViewModel?.RequestSendToFlash();

    private async void OnOpenRecent(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: string path } && this.ViewModel != null && await this.ConfirmDiscardAsync())
        {
            await this.ViewModel.OpenBinAsync(path);
        }
    }

    private async void OnCopyTable(object? sender, RoutedEventArgs e)
    {
        ValueEditorViewModel? editor = this.ViewModel?.Editor;
        if (editor == null || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        await clipboard.SetTextAsync(editor.ToTabText());
        this.ViewModel!.Status = "Copied " + editor.Name + " to the clipboard.";
    }

    private async void OnSwapSegments(object? sender, RoutedEventArgs e)
    {
        TuneViewModel? vm = this.ViewModel;
        if (vm?.BinData == null)
        {
            if (vm != null)
            {
                vm.Status = "Open a BIN and its XDF first.";
            }

            return;
        }

        SegmentSwapViewModel model = new(vm.BinName, vm.BinData);
        await this.ShowOverlayAsync(new SegmentSwapView(vm, model));
    }

    private async void OnVeWizard(object? sender, RoutedEventArgs e)
    {
        TuneViewModel? vm = this.ViewModel;
        if (vm?.Editor is not { Is2D: true } editor)
        {
            if (vm != null)
            {
                vm.Status = "Select a table with several rows and columns first (for example the main VE table), then open the wizard.";
            }

            return;
        }

        VeWizardView wizard = new(new VeWizardViewModel(editor), vm.LogFolderProvider);
        await this.ShowOverlayAsync(wizard);
        if (wizard.Applied)
        {
            this.BuildEditor();
            vm.Status = "VE wizard: the corrections are in the table (not saved yet). Undo reverts them in one step.";
        }
    }

    private async void OnProfiles(object? sender, RoutedEventArgs e)
    {
        TuneViewModel? vm = this.ViewModel;
        if (vm == null)
        {
            return;
        }

        string folder = vm.ProfilesFolderProvider?.Invoke() ?? LogProfileCatalog.SharedFolder;
        await this.ShowOverlayAsync(new ProfilesView(folder, vm.UseProfile));
    }

    private async void OnUndoSwap(object? sender, RoutedEventArgs e)
    {
        if (this.ViewModel != null)
        {
            await this.ViewModel.UndoSwapAsync();
        }
    }

    private void OnApplyPatch(object? sender, RoutedEventArgs e) => this.ViewModel?.Patch?.Apply();

    private void OnRevertPatch(object? sender, RoutedEventArgs e) => this.ViewModel?.Patch?.Revert();

    private void OnClearCompare(object? sender, RoutedEventArgs e) => this.ViewModel?.ClearComparison();

    private async void OnCheckChecksums(object? sender, RoutedEventArgs e)
    {
        if (this.ViewModel != null)
        {
            await this.ViewModel.CheckChecksumsAsync();
        }
    }

    /// <summary>True when there are no unsaved edits, or the user agrees to throw them away.</summary>
    public async Task<bool> ConfirmDiscardAsync()
    {
        if (this.ViewModel is not { IsDirty: true })
        {
            return true;
        }

        return await this.ConfirmAsync(
            "Unsaved edits", "You have tuning edits that are not saved. Discard them?", "Discard edits");
    }

    /// <summary>A question shown over the screen (no separate window). Returns true for the first button.</summary>
    public async Task<bool> ConfirmAsync(string title, string text, string yesLabel)
    {
        ConfirmPage page = new(title, text, yesLabel, "Cancel");
        await this.ShowOverlayAsync(page);
        return page.Answer;
    }

    /// <summary>
    /// Shows a page over the Tune screen and waits until it closes. Used instead of pop-up windows so the same
    /// screens work in a single-view host such as a phone.
    /// </summary>
    public Task ShowOverlayAsync(OverlayPage page)
    {
        page.Confirm ??= this.ConfirmAsync;

        TaskCompletionSource done = new();
        Border card = new()
        {
            Background = this.PaneBackground(),
            BorderBrush = this.TryFindResource("ControlElevationBorderBrush", this.ActualThemeVariant, out object? b) && b is IBrush brush
                ? brush
                : Brushes.Gray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Margin = new Thickness(12),
            MaxWidth = 820,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = page.StretchHeight ? VerticalAlignment.Stretch : VerticalAlignment.Center,
            Child = page,
        };

        // The dimmed backdrop also stops clicks reaching the screen underneath.
        Grid backdrop = new() { Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), Children = { card } };
        this.OverlayLayer.Children.Add(backdrop);
        this.OverlayLayer.IsVisible = true;

        page.CloseRequested += (_, _) =>
        {
            this.OverlayLayer.Children.Remove(backdrop);
            this.OverlayLayer.IsVisible = this.OverlayLayer.Children.Count > 0;
            done.TrySetResult();
        };

        return done.Task;
    }

    private void OnUndo(object? sender, RoutedEventArgs e)
    {
        if (this.ViewModel == null)
        {
            return;
        }

        this.ViewModel.UndoEdit();

        // The bytes of the BIN changed under the cells: show them again.
        if (this.ViewModel.Editor != null && this.cells != null)
        {
            ValueEditorViewModel editor = this.ViewModel.Editor;
            if (editor.IsFlag)
            {
                this.BuildEditor();
                return;
            }

            for (int r = 0; r < editor.Rows; r++)
            {
                for (int c = 0; c < editor.Columns; c++)
                {
                    this.cells[r, c].Text = editor.CellText(r, c);
                }
            }

            Recolor(editor, this.cells);
        }
        else
        {
            this.BuildEditor();
        }
    }

    private async void OnSaveAs(object? sender, RoutedEventArgs e)
    {
        TopLevel? top = TopLevel.GetTopLevel(this);
        if (top == null || this.ViewModel == null)
        {
            return;
        }

        string baseName = Path.GetFileNameWithoutExtension(this.ViewModel.BinPath ?? "tune");
        IStorageFile? file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save BIN as",
            SuggestedFileName = baseName + "_edit.bin",
            DefaultExtension = "bin",
            FileTypeChoices = new[] { new FilePickerFileType("BIN files") { Patterns = new[] { "*.bin" } } },
        });

        string? path = file?.TryGetLocalPath();
        if (path != null)
        {
            await this.ViewModel.SaveAsAsync(path);
        }
    }

    private async Task<string?> PickFileAsync(string title, string typeName, params string[] patterns)
    {
        TopLevel? top = TopLevel.GetTopLevel(this);
        if (top == null)
        {
            return null;
        }

        string? folder = typeName == "BIN files" ? this.ViewModel?.LastFolder : null;
        IStorageFolder? start = folder != null && Directory.Exists(folder)
            ? await top.StorageProvider.TryGetFolderFromPathAsync(folder)
            : null;

        IReadOnlyList<IStorageFile> files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = start,
            FileTypeFilter = new[] { new FilePickerFileType(typeName) { Patterns = patterns } },
        });

        return files.Count == 1 ? files[0].TryGetLocalPath() : null;
    }
}
