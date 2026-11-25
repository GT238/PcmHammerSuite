using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PcmSuite.Tuning.Core.Models;

namespace PcmSuite.Tune;

/// <summary>Which table cell a text box shows.</summary>
internal sealed record CellTag(int Row, int Column);

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

        // Right-click (or press and hold) on a table in the list shows its menu as one of the Tuner's own pages. Avalonia's pop-up menu
        // opened but was not drawn in this window, and a page works the same on every platform. Handled on the whole tree, so it works wherever in a row the click lands.
        this.NodeTree.AddHandler(ContextRequestedEvent, this.OnNodeContextRequested, RoutingStrategies.Tunnel, handledEventsToo: true);

        // Drop a .bin or .xdf file anywhere on the Tuner to open it.
        DragDrop.SetAllowDrop(this, true);
        this.AddHandler(DragDrop.DragOverEvent, this.OnDragOver);
        this.AddHandler(DragDrop.DropEvent, this.OnDrop);

        this.SizeChanged += (_, e) => this.ApplyLayout(e.NewSize.Width);
        this.AttachedToVisualTree += (_, _) => this.WatchForReads();
        this.EditorScroll.ScrollChanged += (_, _) => this.UpdateFrozenHeaders();

        // The grid takes the keyboard (arrows, +/-, Ctrl+C/V/Z/Y) and the mouse (drag to select).
        this.GridHost.Focusable = true;
        this.GridHost.FocusAdorner = null;
        this.GridHost.KeyDown += this.OnGridKeyDown;
        this.GridHost.PointerMoved += this.OnGridPointerMoved;
        this.GridHost.PointerReleased += this.OnGridPointerReleased;
        this.historyFlyout = new Flyout { Content = this.historyList };
        this.historyFlyout.Opened += this.OnHistoryOpened;
        this.HistoryButton.Flyout = this.historyFlyout;
    }

    private const double WideWidth = 900;

    private bool wide = true;
    private int page; // narrow layout: 0 parameters, 1 editor, 2 details

    private bool descOpen;
    private bool applyingLayout;

    private void OnDescToggled(object? sender, RoutedEventArgs e)
    {
        if (!this.applyingLayout && this.wide)
        {
            this.descOpen = this.DescExpander.IsExpanded;
        }
    }

    private void OnHideListChanged(object? sender, RoutedEventArgs e)
    {
        this.ListToggle.Content = this.ListToggle.IsChecked == true ? "Show list" : "Hide list";
        this.ApplyLayout(this.Bounds.Width);
    }

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

        this.ListToggle.IsVisible = this.wide;

        // The description is folded away on a wide window (the table gets the height) and always open on its own page.
        this.applyingLayout = true;
        this.DescExpander.IsExpanded = !this.wide || this.descOpen;
        this.applyingLayout = false;
        if (this.wide)
        {
            bool hideList = this.ListToggle.IsChecked == true;
            this.Body.ColumnDefinitions[0].Width = new GridLength(hideList ? 0 : 360);
            this.Body.ColumnDefinitions[1].Width = new GridLength(hideList ? 0 : 8);
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
            this.TreePane.IsVisible = this.ListToggle.IsChecked != true;
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

        // Without a Logger to follow (the standalone Tuner) the Live button stays visible but greyed out, to show what is planned.
        bool hasLiveSource = this.attached?.LiveSource != null;
        this.LiveToggle.IsEnabled = hasLiveSource;
        ToolTip.SetTip(this.LiveToggle, hasLiveSource
            ? "Outline the cell the engine is in right now, from the Logger's engine speed and MAP readings"
            : "Coming later: needs a connection to the Logger. Available in the full PcmSuite.");

        this.BuildEditor();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Typing in the search box marks the matching trouble code rows of the open table.
        if (e.PropertyName == nameof(TuneViewModel.SearchText) && this.attached?.Editor is { } open && open.HasRowLabels
            && Enumerable.Range(0, open.Rows).Any(r => DtcCatalog.IsCode(open.RowLabel(r))))
        {
            this.BuildEditor();
        }

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

    // ---- Live trace: outline the cell the engine is in, from the Logger's readings ----

    private LiveTraceViewModel? trace;
    private DispatcherTimer? liveTimer;
    private (int Row, int Column)? liveCell;

    private void OnLiveChanged(object? sender, RoutedEventArgs e) => this.StartLiveIfOn();

    private void StopLive()
    {
        this.liveTimer?.Stop();
        this.liveTimer = null;
        this.trace = null;
        (int Row, int Column)? old = this.liveCell;
        this.liveCell = null;
        if (old != null)
        {
            this.StyleBorder(old.Value.Row, old.Value.Column);
        }

        this.LiveStatus.IsVisible = false;
    }

    private void StartLiveIfOn()
    {
        this.StopLive();
        ValueEditorViewModel? editor = this.ViewModel?.Editor;
        if (this.LiveToggle.IsChecked != true || editor == null || !editor.Is2D)
        {
            return;
        }

        this.LiveStatus.IsVisible = true;
        ILiveSource? source = this.ViewModel?.LiveSource;
        if (source == null)
        {
            this.LiveStatus.Text = "No Logger is attached.";
            return;
        }

        this.trace = new LiveTraceViewModel(editor, source);
        this.LiveStatus.Text = this.trace.Status;
        if (!this.trace.CanTrace)
        {
            return;
        }

        this.liveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        this.liveTimer.Tick += (_, _) => this.ShowLive();
        this.liveTimer.Start();
        this.ShowLive();
    }

    private void ShowLive()
    {
        if (this.trace == null)
        {
            return;
        }

        this.trace.Update();
        this.LiveStatus.Text = this.trace.Status;
        (int Row, int Column)? next = this.trace.Row >= 0 ? (this.trace.Row, this.trace.Column) : null;
        if (next == this.liveCell)
        {
            return;
        }

        (int Row, int Column)? old = this.liveCell;
        this.liveCell = next;
        if (old != null)
        {
            this.StyleBorder(old.Value.Row, old.Value.Column);
        }

        if (next != null)
        {
            this.StyleBorder(next.Value.Row, next.Value.Column);
        }
    }

    /// <summary>Frame for one cell: cyan for the live cell, orange when it differs from the reference BIN.</summary>
    private void StyleBorder(int row, int column)
    {
        ValueEditorViewModel? editor = this.ViewModel?.Editor;
        if (this.cells == null || editor == null || row >= this.cells.GetLength(0) || column >= this.cells.GetLength(1))
        {
            return;
        }

        bool live = this.liveCell is { } cell && cell.Row == row && cell.Column == column;
        bool selected = editor.Selection is { } range && range.Contains(row, column);
        bool changed = editor.IsChanged(row, column);
        TextBox box = this.cells[row, column];
        box.BorderBrush = live ? Brushes.DeepSkyBlue : selected ? Brushes.White : changed ? Brushes.Orange : null;
        box.BorderThickness = live ? new Thickness(3) : selected || changed ? new Thickness(2) : new Thickness(1);
    }

    // ---- Spreadsheet-style editing: select cells, then type, nudge, fill, paste, scale, interpolate, smooth ----

    private ValueEditorViewModel? hooked;
    private TextBox? editingBox;
    private bool dragSelecting;
    private CellRange? shownSelection;

    private void Hook(ValueEditorViewModel? editor)
    {
        if (this.hooked != null)
        {
            this.hooked.SelectionChanged -= this.OnSelectionChanged;
            this.hooked.DataChanged -= this.OnEditorDataChanged;
        }

        this.hooked = editor;
        this.shownSelection = null;
        this.editingBox = null;
        this.SelectionText.Text = string.Empty;
        if (editor != null)
        {
            editor.SelectionChanged += this.OnSelectionChanged;
            editor.DataChanged += this.OnEditorDataChanged;
        }
    }

    private void OnSelectionChanged()
    {
        ValueEditorViewModel? editor = this.hooked;
        if (editor == null || this.cells == null)
        {
            return;
        }

        // Restyle only the cells that entered or left the selection.
        CellRange? before = this.shownSelection;
        CellRange? after = editor.Selection;
        this.shownSelection = after;
        int row0 = Math.Min(before?.Row0 ?? int.MaxValue, after?.Row0 ?? int.MaxValue);
        int col0 = Math.Min(before?.Col0 ?? int.MaxValue, after?.Col0 ?? int.MaxValue);
        int row1 = Math.Max(before?.Row1 ?? -1, after?.Row1 ?? -1);
        int col1 = Math.Max(before?.Col1 ?? -1, after?.Col1 ?? -1);
        for (int r = Math.Max(0, row0); r <= Math.Min(row1, editor.Rows - 1); r++)
        {
            for (int c = Math.Max(0, col0); c <= Math.Min(col1, editor.Columns - 1); c++)
            {
                this.StyleBorder(r, c);
            }
        }

        this.SelectionText.Text = editor.SelectionStats();
    }

    /// <summary>The BIN changed under the cells (an edit, undo or redo): show the new values.</summary>
    private void OnEditorDataChanged()
    {
        ValueEditorViewModel? editor = this.hooked;
        if (editor == null || this.cells == null || editor.IsFlag)
        {
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
        for (int r = 0; r < editor.Rows; r++)
        {
            for (int c = 0; c < editor.Columns; c++)
            {
                this.StyleBorder(r, c);
            }
        }

        this.SelectionText.Text = editor.SelectionStats();
    }

    private void BeginEdit(int row, int column, string? initial)
    {
        if (this.cells == null)
        {
            return;
        }

        this.EndEdit(true, 0);
        TextBox box = this.cells[row, column];
        this.editingBox = box;
        box.IsReadOnly = false;
        box.Focus();
        if (initial != null)
        {
            box.Text = initial;
            box.CaretIndex = initial.Length;
        }
        else
        {
            box.SelectAll();
        }
    }

    /// <summary>Finish typing into a cell. Commit stores the text; moveRows then steps the selection (Enter moves down).</summary>
    private void EndEdit(bool commit, int moveRows)
    {
        TextBox? box = this.editingBox;
        ValueEditorViewModel? editor = this.hooked;
        if (box == null || editor == null || box.Tag is not CellTag(var row, var column))
        {
            this.editingBox = null;
            return;
        }

        this.editingBox = null;
        box.IsReadOnly = true;
        if (commit)
        {
            editor.TryCommit(row, column, box.Text ?? string.Empty, out _);
        }

        box.Text = editor.CellText(row, column);
        this.GridHost.Focus();
        if (moveRows != 0)
        {
            editor.Move(moveRows, 0, false);
        }
    }

    private void OnCellPressed(TextBox box, PointerPressedEventArgs e)
    {
        ValueEditorViewModel? editor = this.hooked;
        if (editor == null || box.Tag is not CellTag(var row, var column) || !e.GetCurrentPoint(box).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (this.editingBox == box)
        {
            return; // clicking inside the cell being typed in: let the text box handle it
        }

        e.Handled = true;
        if (e.ClickCount >= 2 || editor.IsSingleValue)
        {
            editor.Select(row, column);
            this.BeginEdit(row, column, null);
            return;
        }

        this.EndEdit(true, 0);
        editor.Select(row, column, e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        this.GridHost.Focus();
        this.dragSelecting = true;
        e.Pointer.Capture(this.GridHost);
    }

    private void OnGridPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!this.dragSelecting || this.hooked == null)
        {
            return;
        }

        Visual? hit = this.GridHost.InputHitTest(e.GetPosition(this.GridHost)) as Visual;
        while (hit != null && hit is not TextBox { Tag: CellTag })
        {
            hit = hit.GetVisualParent();
        }

        if (hit is TextBox { Tag: CellTag(var row, var column) } && this.hooked.Anchor != null)
        {
            if (this.hooked.Extent != (row, column))
            {
                this.hooked.Select(row, column, true);
            }
        }
    }

    private void OnGridPointerReleased(object? sender, PointerReleasedEventArgs e) => this.dragSelecting = false;

    private async void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        ValueEditorViewModel? editor = this.hooked;
        if (editor == null || this.cells == null || this.editingBox != null)
        {
            return;
        }

        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        e.Handled = true;
        switch (e.Key)
        {
            case Key.Up: editor.Move(-1, 0, shift); return;
            case Key.Down: editor.Move(1, 0, shift); return;
            case Key.Left: editor.Move(0, -1, shift); return;
            case Key.Right: editor.Move(0, 1, shift); return;
            case Key.Escape: editor.ClearSelection(); return;
            case Key.Enter or Key.F2 when editor.Anchor is { } cell:
                this.BeginEdit(cell.Row, cell.Column, null);
                return;
            case Key.Add or Key.OemPlus:
                editor.Nudge(shift ? 10 : 1);
                return;
            case Key.Subtract or Key.OemMinus:
                editor.Nudge(shift ? -10 : -1);
                return;
            case Key.A when ctrl: editor.SelectAll(); return;
            case Key.W when ctrl:
                if (this.ViewModel?.ActiveTab is { } active)
                {
                    this.ViewModel.CloseTab(active);
                }

                return;
            case Key.I when ctrl: editor.Interpolate(); return;
            case Key.Z when ctrl: this.ViewModel?.UndoEdit(); return;
            case Key.Y when ctrl: this.ViewModel?.RedoEdit(); return;
            case Key.C when ctrl:
                if (editor.SelectionText() is { } copied && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboardOut)
                {
                    await clipboardOut.SetTextAsync(copied);
                }

                return;
            case Key.V when ctrl:
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboardIn && await clipboardIn.TryGetTextAsync() is { } pasted)
                {
                    editor.Paste(pasted);
                }

                return;
        }

        // A digit or decimal point starts typing into the selected cell, as in a spreadsheet.
        if (!ctrl && e.KeySymbol is { Length: 1 } symbol && (char.IsDigit(symbol[0]) || symbol[0] == '.') && editor.Anchor is { } start)
        {
            this.BeginEdit(start.Row, start.Column, symbol);
            return;
        }

        e.Handled = false;
    }

    private void OnInterpolate(object? sender, RoutedEventArgs e) => this.hooked?.Interpolate();

    private void OnSmooth(object? sender, RoutedEventArgs e) => this.hooked?.Smooth();

    private void OnScale(object? sender, RoutedEventArgs e)
    {
        if (this.hooked != null && this.ScaleBox.Value is { } factor)
        {
            this.hooked.Scale((double)factor);
        }
    }

    private void OnCloseTab(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: EditorTab tab })
        {
            this.ViewModel?.CloseTab(tab);
        }
    }

    private void OnRedo(object? sender, RoutedEventArgs e) => this.ViewModel?.RedoEdit();

    private readonly StackPanel historyList = new() { Spacing = 2, MinWidth = 260 };
    private readonly Flyout historyFlyout;

    private void OnHistoryItem(object? sender, RoutedEventArgs e)
    {
        // The list is newest first, so the clicked row's index is how many edits to take back, plus itself.
        if (sender is Button { Tag: int index } && this.hooked != null)
        {
            this.historyFlyout.Hide();
            this.hooked.UndoSteps(index + 1);
        }
    }

    private void OnHistoryOpened(object? sender, EventArgs e)
    {
        this.historyList.Children.Clear();
        IReadOnlyList<string> history = this.hooked?.History ?? Array.Empty<string>();
        if (history.Count == 0)
        {
            this.historyList.Children.Add(new TextBlock { Text = "No edits yet", Opacity = 0.7 });
        }

        for (int i = 0; i < history.Count; i++)
        {
            Button item = new() { Content = history[i], Tag = i, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
            item.Click += this.OnHistoryItem;
            this.historyList.Children.Add(item);
        }
    }

    private void BuildEditor()
    {
        this.StopLive();
        this.cells = null;
        this.columnHeaders.Clear();
        this.rowHeaders.Clear();
        this.GridHost.Content = null;

        ValueEditorViewModel? editor = this.ViewModel?.Editor;
        this.Surface.Editor = editor;
        this.Hook(editor);
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
                int headerColumn = c;
                header.PointerPressed += (_, _) =>
                {
                    this.EndEdit(true, 0);
                    editor.SelectColumn(headerColumn);
                    this.GridHost.Focus();
                };
                this.columnHeaders.Add(header);
                grid.Children.Add(header);
            }
        }

        TextBlock? firstMatch = null;
        if (editor.HasRowLabels)
        {
            for (int r = 0; r < editor.Rows; r++)
            {
                TextBlock header = this.FrozenHeader(editor.RowLabel(r), rowOffset + r, 0);
                int headerRow = r;
                header.PointerPressed += (_, _) =>
                {
                    this.EndEdit(true, 0);
                    editor.SelectRow(headerRow);
                    this.GridHost.Focus();
                };
                if (DtcCatalog.Matches(editor.RowLabel(r), this.ViewModel?.SearchText))
                {
                    header.Foreground = new SolidColorBrush(Color.Parse("#E8A838"));
                    firstMatch ??= header;
                }

                this.rowHeaders.Add(header);
                grid.Children.Add(header);

                // A trouble code row gets its meaning beside the cell (generic codes only; see DtcCatalog).
                if (DtcCatalog.Describe(editor.RowLabel(r)) is { } meaning)
                {
                    TextBlock note = new()
                    {
                        Text = meaning,
                        Opacity = 0.7,
                        FontSize = 12,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(10, 0, 0, 0),
                    };
                    Grid.SetRow(note, rowOffset + r);
                    Grid.SetColumn(note, colOffset + editor.Columns);
                    grid.Children.Add(note);
                }
            }
        }

        TextBox[,] boxes = new TextBox[editor.Rows, editor.Columns];
        for (int r = 0; r < editor.Rows; r++)
        {
            for (int c = 0; c < editor.Columns; c++)
            {
                TextBox box = new()
                {
                    Text = editor.CellText(r, c),
                    MinWidth = 56,
                    TextAlignment = TextAlignment.Center,
                    Padding = new Thickness(2, 2),
                    MinHeight = 28,   // the theme's default (32) is what made the rows 34 px apart
                    Margin = new Thickness(1),
                    FontFamily = new FontFamily("Consolas, Menlo, DejaVu Sans Mono, monospace"),
                    IsReadOnly = true,   // typing starts only after a double click, Enter or a digit key
                    Tag = new CellTag(r, c),
                };
                box.AddHandler(InputElement.PointerPressedEvent, (_, e) => this.OnCellPressed(box, e), RoutingStrategies.Tunnel);
                box.LostFocus += (_, _) =>
                {
                    if (this.editingBox == box)
                    {
                        this.EndEdit(true, 0);
                    }
                };
                box.KeyDown += (_, e) =>
                {
                    if (this.editingBox != box)
                    {
                        return;
                    }

                    if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                    {
                        // Shift+Enter: put the typed value into every selected cell.
                        string typed = box.Text ?? string.Empty;
                        this.EndEdit(false, 0);
                        editor.FillText(typed);
                        e.Handled = true;
                    }
                    else if (e.Key == Key.Enter)
                    {
                        this.EndEdit(true, 1);
                        e.Handled = true;
                    }
                    else if (e.Key == Key.Escape)
                    {
                        this.EndEdit(false, 0);
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

        if (firstMatch != null)
        {
            // Scroll the first matching trouble code into view once the grid has been laid out.
            TextBlock target = firstMatch;
            Dispatcher.UIThread.Post(() => target.BringIntoView(), DispatcherPriority.Loaded);
        }
        this.UpdateFrozenHeaders();
        this.StartLiveIfOn();
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
            await this.OpenBinAskingAsync(path);
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

    private async void OnEditDefinition(object? sender, RoutedEventArgs e)
    {
        if (this.ViewModel is { Editor: { } editor } vm)
        {
            await this.ShowOverlayAsync(new DefinitionEditorView(vm, DefinitionDraft.From(editor.Table)));
        }
    }

    private async void OnFindTables(object? sender, RoutedEventArgs e)
    {
        TuneViewModel? vm = this.ViewModel;
        if (vm == null || vm.BinData == null)
        {
            if (vm != null)
            {
                vm.Status = "Open a BIN first, then the tables can be looked for in it.";
            }

            return;
        }

        await this.ShowOverlayAsync(new DefinitionFinderView(
            vm,
            () => this.PickFileAsync("Pick a BIN of a similar operating system that has an XDF", "BIN files", true, "*.bin", "*.BIN"),
            () => this.PickFileAsync("Select the XDF for that BIN", "XDF files", "*.xdf", "*.XDF")));
    }

    private async void OnNewTable(object? sender, RoutedEventArgs e)
    {
        TuneViewModel? vm = this.ViewModel;
        if (vm == null || vm.BinData == null)
        {
            if (vm != null)
            {
                vm.Status = "Open a BIN first, then a new table can be described for it.";
            }

            return;
        }

        await this.ShowOverlayAsync(new DefinitionEditorView(vm, DefinitionDraft.Blank(vm.SelectedNode?.Table?.Category ?? "Other")));
    }

    private async void OnSaveDefinition(object? sender, RoutedEventArgs e)
    {
        if (this.ViewModel != null)
        {
            await this.ViewModel.SaveMyDefinitionAsync();
        }
    }

    private async void OnCompare(object? sender, RoutedEventArgs e)
    {
        string? path = await this.PickFileAsync("Pick the reference BIN (for example the stock file)", "BIN files", preferStock: true, "*.bin", "*.BIN");
        if (path != null && this.ViewModel != null)
        {
            await this.ViewModel.CompareAsync(path);
        }
    }

    private async void OnCompareStock(object? sender, RoutedEventArgs e)
    {
        if (this.ViewModel != null)
        {
            await this.ViewModel.CompareToStockAsync();
        }
    }

    private async void OnOpenStockFolder(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(StockBins.Folder);
            await top.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(StockBins.Folder));
        }
        catch (Exception ex)
        {
            this.ViewModel!.Status = "Could not open the Reference BINs folder: " + ex.Message;
        }
    }

    private void OnSendToFlash(object? sender, RoutedEventArgs e) => this.ViewModel?.RequestSendToFlash();

    private async void OnOpenRecent(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { SelectedItem: string path } combo)
        {
            return;
        }

        // The box is a menu, not a setting: put it back to its prompt so the same file can be picked again.
        combo.SelectedItem = null;
        if (this.ViewModel != null && await this.ConfirmDiscardAsync())
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

    private async void OnLogMap(object? sender, RoutedEventArgs e)
    {
        TuneViewModel? vm = this.ViewModel;
        if (vm?.Editor is not { Is2D: true } editor)
        {
            if (vm != null)
            {
                vm.Status = "Select a table with several rows and columns first (for example the main VE table), then open the log map.";
            }

            return;
        }

        await this.ShowOverlayAsync(new LogMapView(new LogMapViewModel(editor), vm.LogFolderProvider));
    }

    private async void OnCompareWorkbench(object? sender, RoutedEventArgs e)
    {
        TuneViewModel? vm = this.ViewModel;
        if (vm?.BinData == null)
        {
            if (vm != null)
            {
                vm.Status = "Open a BIN first.";
            }

            return;
        }

        await this.ShowOverlayAsync(new CompareView(vm));
    }

    private async void OnUndoCopyBack(object? sender, RoutedEventArgs e)
    {
        if (this.ViewModel != null)
        {
            await this.ViewModel.UndoCopyBackAsync();
        }
    }

    private async void OnHexView(object? sender, RoutedEventArgs e)
    {
        TuneViewModel? vm = this.ViewModel;
        if (vm?.BinData == null)
        {
            if (vm != null)
            {
                vm.Status = "Open a BIN first.";
            }

            return;
        }

        await this.ShowOverlayAsync(new HexView(vm));
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

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        TuneViewModel? vm = this.ViewModel;
        if (vm == null)
        {
            return;
        }

        string? path = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).FirstOrDefault(f => f != null);
        e.Handled = true;
        if (path == null)
        {
            return;
        }

        // A project folder, or its project.json, opens that project.
        string? projectFolder = Directory.Exists(path) ? path
            : Path.GetFileName(path).Equals("project.json", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(path)
            : null;
        if (projectFolder != null)
        {
            await this.OpenProjectFolderAsync(projectFolder);
            return;
        }

        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".bin" or ".xdf"))
        {
            vm.Status = "Drop a .bin or .xdf file, or a project folder, to open it. (" + Path.GetFileName(path) + " is none of those.)";
            return;
        }

        if (!await this.ConfirmDiscardAsync())
        {
            return;
        }

        if (extension == ".bin")
        {
            await this.OpenBinAskingAsync(path);
        }
        else
        {
            await vm.OpenXdfAsync(path);
        }
    }

    /// <summary>
    /// Open a BIN, asking first whenever that would switch project or add the file to one:
    /// inside this project, or no project open, it just opens.
    /// </summary>
    private async Task OpenBinAskingAsync(string path)
    {
        TuneViewModel? vm = this.ViewModel;
        if (vm == null)
        {
            return;
        }

        PcmSuite.Tuning.Core.Models.ProjectInfo? current = vm.CurrentProject;
        PcmSuite.Tuning.Core.Models.ProjectInfo? owner = PcmSuite.Tuning.Core.Models.ProjectInfo.FindContaining(path);
        bool inCurrent = current != null && PcmSuite.Tuning.Core.Models.AppPaths.IsInside(path, current.ProjectFolder);
        string name = Path.GetFileName(path);

        if (owner != null && !inCurrent)
        {
            string stay = current != null ? $" (it stays in '{current.DisplayName}')" : string.Empty;
            ChoicePage ask = new(
                "Switch project?",
                $"{name} belongs to project '{owner.DisplayName}'. Switch to that project, or just open the file{stay}?",
                $"Switch to '{owner.DisplayName}'",
                "Just open it",
                "Cancel");
            await this.ShowOverlayAsync(ask);
            if (ask.Choice == 0)
            {
                await vm.ImportBinAsync(path);
            }
            else if (ask.Choice == 1)
            {
                await vm.OpenBinAsync(path);
            }

            return;
        }

        if (current != null && !inCurrent)
        {
            ChoicePage ask = new(
                "Add to the project?",
                $"{name} is not in project '{current.DisplayName}'. Copy it into the project's reads folder, or just open it?",
                "Add to project",
                "Just open it",
                "Cancel");
            await this.ShowOverlayAsync(ask);
            if (ask.Choice == 0)
            {
                await vm.ImportBinAsync(path, true);
            }
            else if (ask.Choice == 1)
            {
                await vm.OpenBinAsync(path);
            }

            return;
        }

        await vm.OpenBinAsync(path);
    }

    /// <summary>Open a project from its folder; a folder without project.json can be made into one.</summary>
    private async Task OpenProjectFolderAsync(string folder)
    {
        TuneViewModel? vm = this.ViewModel;
        if (vm == null)
        {
            return;
        }

        PcmSuite.Tuning.Core.Models.ProjectInfo? project = PcmSuite.Tuning.Core.Models.ProjectInfo.Load(folder);
        if (project == null)
        {
            if (!await this.ConfirmAsync("Make this a project?", $"{folder} is not a project yet (it has no project.json). Make it one? Its files stay where they are.", "Make it a project"))
            {
                return;
            }

            project = vm.AdoptFolder(folder);
        }

        if (await this.ConfirmDiscardAsync())
        {
            await vm.OpenProjectAsync(project);
        }
    }

    private TuneViewModel? watching;

    private void WatchForReads()
    {
        if (this.ViewModel == null || ReferenceEquals(this.watching, this.ViewModel))
        {
            return;
        }

        this.watching = this.ViewModel;
        this.ViewModel.NewReadDetected += path =>
            Dispatcher.UIThread.Post(async () => await this.OfferNewReadAsync(path));
    }

    /// <summary>A read finished in the project's reads folder: offer to open it.</summary>
    private async Task OfferNewReadAsync(string path)
    {
        TuneViewModel? vm = this.ViewModel;
        if (vm == null)
        {
            return;
        }

        string warning = vm.IsDirty ? " Your unsaved edits to the open BIN would be lost." : string.Empty;
        if (await this.ConfirmAsync("New read", $"{Path.GetFileName(path)} has arrived in the project's reads folder. Open it in the Tuner?{warning}", "Open it"))
        {
            await vm.OpenBinAsync(path);
        }
    }

    private void OnHelp(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string topic })
        {
            this.ViewModel?.RequestHelp(topic);
        }
    }

    private async void OnProjects(object? sender, RoutedEventArgs e)
    {
        if (this.ViewModel == null)
        {
            return;
        }

        ProjectsView page = new(this.ViewModel) { Host = this.ShowOverlayAsync };
        await this.ShowOverlayAsync(page);
    }

    /// <summary>The list entry under a click: the nearest control up the tree whose data is a table or folder.</summary>
    private static (Control Target, ParameterNode Node)? NodeAt(object? source)
    {
        for (Visual? v = source as Visual; v != null; v = v.GetVisualParent())
        {
            if (v is Control c && c.DataContext is ParameterNode node && c is not TreeView)
            {
                return (c, node);
            }
        }

        return null;
    }

    private async void OnNodeContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        TuneViewModel? vm = this.ViewModel;
        if (vm == null || NodeAt(e.Source) is not { } hit)
        {
            return;
        }

        e.Handled = true;
        ParameterNode node = hit.Node;
        await this.ShowOverlayAsync(new NodeMenuPage(
            node,
            vm,
            async text =>
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                {
                    await clipboard.SetTextAsync(text);
                }
            },
            () => node.Table == null
                ? Task.CompletedTask
                : this.ShowOverlayAsync(new DefinitionEditorView(vm, DefinitionDraft.From(node.Table)))));
    }

    private async void OnOpenProjectFolder(object? sender, RoutedEventArgs e)
    {
        string? folder = this.ViewModel?.CurrentProject?.ProjectFolder;
        if (folder == null || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        try
        {
            await top.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(folder));
        }
        catch (Exception ex)
        {
            this.ViewModel!.Status = "Could not open the project folder: " + ex.Message;
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

    private async void OnOpenChangeLog(object? sender, RoutedEventArgs e)
    {
        string? log = this.ViewModel?.ChangeLogPath;
        if (this.ViewModel == null || log == null || !File.Exists(log))
        {
            if (this.ViewModel != null)
            {
                this.ViewModel.Status = "No edits have been logged for this BIN yet.";
            }

            return;
        }

        try
        {
            if (TopLevel.GetTopLevel(this) is { } top)
            {
                await top.Launcher.LaunchFileInfoAsync(new FileInfo(log));
            }
        }
        catch (Exception ex)
        {
            this.ViewModel.Status = "Could not open the change log (" + log + "): " + ex.Message;
        }
    }

    private async void OnExportNotes(object? sender, RoutedEventArgs e)
    {
        TopLevel? top = TopLevel.GetTopLevel(this);
        if (top == null || this.ViewModel == null)
        {
            return;
        }

        if (this.ViewModel.BinPath == null)
        {
            this.ViewModel.Status = "Open a BIN first.";
            return;
        }

        string baseName = Path.GetFileNameWithoutExtension(this.ViewModel.BinPath);
        IStorageFile? file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export notes",
            SuggestedFileName = baseName + "_notes.txt",
            DefaultExtension = "txt",
            FileTypeChoices = new[] { new FilePickerFileType("Text files") { Patterns = new[] { "*.txt" } } },
        });

        if (file?.TryGetLocalPath() is { } path)
        {
            try
            {
                await this.ViewModel.ExportNotesAsync(path);
            }
            catch (Exception ex)
            {
                this.ViewModel.Status = "Could not export the notes: " + ex.Message;
            }
        }
    }

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

        // Flags have no cell grid: rebuild their check box. Tables refresh themselves through DataChanged.
        if (this.ViewModel.Editor is { IsFlag: true } || this.ViewModel.Editor == null || this.cells == null)
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
        string? saveFolder = this.ViewModel.SaveFolder;
        IStorageFolder? start = saveFolder != null && Directory.Exists(saveFolder)
            ? await top.StorageProvider.TryGetFolderFromPathAsync(saveFolder)
            : null;
        IStorageFile? file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save BIN as",
            SuggestedStartLocation = start,
            SuggestedFileName = this.ViewModel.SuggestedSaveName(),
            DefaultExtension = "bin",
            FileTypeChoices = new[] { new FilePickerFileType("BIN files") { Patterns = new[] { "*.bin" } } },
        });

        string? path = file?.TryGetLocalPath();
        if (path != null)
        {
            await this.ViewModel.SaveAsAsync(path);
        }
    }

    private async Task<string?> PickFileAsync(string title, string typeName, params string[] patterns) =>
        await this.PickFileAsync(title, typeName, false, patterns);

    /// <summary>With preferStock, BIN pickers open in the Reference BINs folder when it holds any BINs.</summary>
    private async Task<string?> PickFileAsync(string title, string typeName, bool preferStock, params string[] patterns)
    {
        TopLevel? top = TopLevel.GetTopLevel(this);
        if (top == null)
        {
            return null;
        }

        string? folder = typeName == "BIN files" ? this.ViewModel?.LastFolder : null;
        IStorageFolder? start = preferStock && typeName == "BIN files"
            ? await PickerStart.StockBinsAsync(top, folder)
            : folder != null && Directory.Exists(folder)
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
