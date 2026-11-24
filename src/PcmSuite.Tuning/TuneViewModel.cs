using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PcmSuite.Tuning.Core.Models;
using PcmSuite.Tuning.Core.Parsers;

namespace PcmSuite.Tune;

/// <summary>
/// The Tune tab: open a BIN, find or pick its XDF, and browse the parameter tree.
/// Ported from LS-Studio's MainViewModel, trimmed to the first step (no pins, notes or tags yet).
/// </summary>
public partial class TuneViewModel : ObservableObject
{
    [ObservableProperty] private string _status = "Open a BIN file to begin.";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _binName = "No BIN loaded";
    [ObservableProperty] private string _xdfName = "No XDF loaded";
    [ObservableProperty] private string _details = "Select a parameter to see its details.";
    [ObservableProperty] private ParameterNode? _selectedNode;
    [ObservableProperty] private ValueEditorViewModel? _editor;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOfferStock))]
    private bool _isComparing;

    // A BIN found in the shared Reference BINs folder with the same part numbers as the open one.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanOfferStock))]
    [NotifyPropertyChangedFor(nameof(StockBinLabel))]
    private string? _stockBinPath;

    [ObservableProperty] private bool _changedOnly;
    [ObservableProperty] private bool _showSurface;
    [ObservableProperty] private bool _hasSelectedTable;
    [ObservableProperty] private bool _selectedPinned;
    [ObservableProperty] private string _selectedTag = "(none)";
    [ObservableProperty] private string _selectedNote = string.Empty;
    [ObservableProperty] private string _tagFilter = "All tags";
    [ObservableProperty] private string _referenceName = string.Empty;

    private List<ParameterNode> allCategories = new();
    private Dictionary<string, TableDefinition> tablesByName = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<ParameterNode> Categories { get; } = new();

    // ---- Open tables (tabs): each keeps its own editor, so undo history survives switching tables ----

    public ObservableCollection<EditorTab> OpenTabs { get; } = new();

    [ObservableProperty] private EditorTab? _activeTab;

    partial void OnActiveTabChanged(EditorTab? value)
    {
        // Chosen from the tab strip: select the same table in the tree, which shows its editor.
        if (value != null && !ReferenceEquals(value.Editor, this.Editor))
        {
            this.SelectedNode = value.Node;
        }
    }

    /// <summary>Close a tab. Edits already made stay in the BIN; only that table's undo history is dropped.</summary>
    public void CloseTab(EditorTab tab)
    {
        int index = this.OpenTabs.IndexOf(tab);
        if (index < 0)
        {
            return;
        }

        bool wasActive = ReferenceEquals(tab, this.ActiveTab);
        this.OpenTabs.RemoveAt(index);
        if (!wasActive)
        {
            return;
        }

        if (this.OpenTabs.Count > 0)
        {
            this.SelectedNode = this.OpenTabs[Math.Min(index, this.OpenTabs.Count - 1)].Node;
        }
        else
        {
            this.ActiveTab = null;
            this.SelectedNode = null;
            this.Editor = null;
        }
    }

    private void CloseAllTabs()
    {
        this.OpenTabs.Clear();
        this.ActiveTab = null;
    }

    public string? BinPath { get; private set; }

    public string? XdfPath { get; private set; }

    public byte[]? BinData { get; private set; }

    /// <summary>True when the BIN is open without any XDF: no tables, but the hex view, checksums and segment swap work.</summary>
    public bool IsBinOnly { get; private set; }

    /// <summary>The first and last byte of a table in the BIN (end is exclusive), for the hex view.</summary>
    public static (int Start, int End) Extent(TableDefinition t)
    {
        int size = Math.Max(1, t.SizeInBytes);
        int rowStep = t.MajorStrideBits > 0 ? t.MajorStrideBits / 8 : t.Columns * size;
        int colStep = t.MinorStrideBits > 0 ? t.MinorStrideBits / 8 : size;
        int start = (int)t.Address;
        return (start, start + Math.Max(0, t.Rows - 1) * rowStep + Math.Max(0, t.Columns - 1) * colStep + size);
    }

    /// <summary>The name of the smallest XDF table that covers this offset, or null.</summary>
    public string? TableNameAt(int offset)
    {
        string? best = null;
        int bestLength = int.MaxValue;
        foreach (TableDefinition t in this.tablesByName.Values)
        {
            (int start, int end) = Extent(t);
            if (offset >= start && offset < end && end - start < bestLength)
            {
                best = t.Name;
                bestLength = end - start;
            }
        }

        return best;
    }

    partial void OnSearchTextChanged(string value) => this.ApplyFilter(value);

    partial void OnChangedOnlyChanged(bool value) => this.ApplyFilter(this.SearchText);

    /// <summary>The BIN to compare against, or null when not comparing.</summary>
    public byte[]? ReferenceData { get; private set; }

    /// <summary>Compare the loaded BIN with another BIN of the same size (for example the stock file).</summary>
    public async Task CompareAsync(string path)
    {
        if (this.BinData == null)
        {
            this.Status = "Open a BIN and its XDF first.";
            return;
        }

        try
        {
            byte[] reference = await Task.Run(() => File.ReadAllBytes(path));
            if (reference.Length != this.BinData.Length)
            {
                this.Status = $"Cannot compare: {Path.GetFileName(path)} is {reference.Length:N0} bytes, " +
                              $"the loaded BIN is {this.BinData.Length:N0}.";
                return;
            }

            this.ReferenceData = reference;
            this.ReferenceName = Path.GetFileName(path);
            this.IsComparing = true;
            await this.RecomputeChangesAsync();
            this.ShowEditor(this.SelectedNode, true);

            // Show the result: list only the tables that differ (the box can be unticked to see everything again).
            if (this.AllLeaves().Any(l => l.HasChanges))
            {
                string summary = this.Status;
                this.ChangedOnly = true;
                this.Status = summary.Replace("tick \"Changed tables only\" to list them.", "The list shows only those; untick \"Changed tables only\" to see all.");
            }
        }
        catch (Exception ex)
        {
            this.Status = "Compare failed: " + ex.Message;
        }
    }

    public void ClearComparison()
    {
        this.ReferenceData = null;
        this.ReferenceName = string.Empty;
        this.IsComparing = false;
        this.ChangedOnly = false;
        foreach (ParameterNode leaf in this.AllLeaves())
        {
            leaf.HasChanges = false;
        }

        this.ShowEditor(this.SelectedNode, true);
        this.Status = "Comparison cleared.";
    }

    private async Task RecomputeChangesAsync()
    {
        byte[]? bin = this.BinData;
        byte[]? reference = this.ReferenceData;
        if (bin == null || reference == null)
        {
            return;
        }

        this.Status = "Comparing...";
        List<ParameterNode> leaves = this.AllLeaves().Where(l => l.Table != null).ToList();
        bool[] differs = await Task.Run(() =>
            leaves.Select(l => ValueEditorViewModel.DiffersFromReference(l.Table!, bin, reference)).ToArray());

        int count = 0;
        for (int i = 0; i < leaves.Count; i++)
        {
            leaves[i].HasChanges = differs[i];
            if (differs[i])
            {
                count++;
            }
        }

        this.Status = count == 0
            ? $"Identical to {this.ReferenceName}: no table differs."
            : $"{count} table(s) differ from {this.ReferenceName}. Marked with a dot; tick \"Changed tables only\" to list them.";
    }

    /// <summary>Where the Logger writes its log files, so the VE wizard can offer the newest one.</summary>
    public Func<string?>? LogFolderProvider { get; set; }

    /// <summary>Live readings from the Logger, for the live trace on a table. Null when no Logger is attached.</summary>
    public ILiveSource? LiveSource { get; set; }

    // ---- Logging profiles for tuning tasks ----

    /// <summary>The folder with the .LogProfile files offered for tuning tasks.</summary>
    public Func<string?>? ProfilesFolderProvider { get; set; }

    /// <summary>Raised when the user picks a profile; the shell hands it to the Logger tab.</summary>
    public event Action<string>? UseProfileRequested;

    public void UseProfile(string path) => this.UseProfileRequested?.Invoke(path);

    // ---- Hand-off to the Flash tab ----

    /// <summary>The saved BIN that passed PCMHammer's checksum validation and has no edits since; null otherwise.</summary>
    [ObservableProperty] private string? _readyToFlashPath;

    /// <summary>Raised when the user asks to flash the saved file; the shell switches to the Flash tab.</summary>
    public event Action<string>? SendToFlashRequested;

    public void RequestSendToFlash()
    {
        if (this.ReadyToFlashPath != null)
        {
            this.SendToFlashRequested?.Invoke(this.ReadyToFlashPath);
        }
    }

    partial void OnIsDirtyChanged(bool value)
    {
        // Any edit after saving means the file on disk is no longer what is open.
        if (value)
        {
            this.ReadyToFlashPath = null;
        }
    }

    // ---- Segment swap ----

    [ObservableProperty] private bool _canUndoSwap;

    // What each swap overwrote: Undo puts back one swap at a time, newest first.
    private readonly SwapHistory swapHistory = new();

    /// <summary>Copy the chosen segments from a donor BIN into the open BIN, in memory.</summary>
    public async Task ApplySegmentsAsync(byte[] donor, IReadOnlyList<PcmSuite.Tuning.Core.Models.SegmentInfo> segments, string donorName)
    {
        if (this.BinData == null || segments.Count == 0)
        {
            return;
        }

        this.swapHistory.Apply(this.BinData, donor, segments);
        this.CanUndoSwap = true;
        this.IsDirty = true;
        this.AppendChangeLog($"Segment swap from {donorName}: " + string.Join(", ", segments.Select(s => s.Name)));
        await this.AfterBytesChangedAsync();
        this.Status = $"Copied {segments.Count} segment(s) from {donorName} into the open BIN (not saved yet). " +
                      "Use Save As to write it with corrected checksums, or Undo segment swap.";
    }

    public async Task UndoSwapAsync()
    {
        if (this.BinData == null || this.swapHistory.Undo(this.BinData) is not { } description)
        {
            return;
        }

        this.CanUndoSwap = this.swapHistory.Count > 0;

        // The BIN differs from whatever was saved last, so it counts as modified (and is no longer "ready to flash").
        this.IsDirty = true;
        this.AppendChangeLog("Segment swap undone: " + description);
        await this.AfterBytesChangedAsync();
        this.Status = $"Segment swap undone: {description} (not saved yet)." +
                      (this.CanUndoSwap ? $" {this.swapHistory.Count} earlier swap(s) can still be undone." : string.Empty);
    }

    // ---- Compare workbench: copy tables from another BIN ----

    [ObservableProperty] private bool _canUndoCopyBack;

    private readonly CopyBackHistory copyBackHistory = new();

    /// <summary>The XDF's tables, for the compare workbench.</summary>
    public IReadOnlyCollection<TableDefinition> AllTables => this.tablesByName.Values;

    /// <summary>Copy whole tables from another BIN into the open BIN, in memory.</summary>
    public async Task CopyTablesFromAsync(byte[] source, IReadOnlyList<TableDefinition> tables, string sourceName)
    {
        if (this.BinData == null || tables.Count == 0 || source.Length != this.BinData.Length)
        {
            return;
        }

        this.copyBackHistory.Apply(this.BinData, source, tables);
        this.CanUndoCopyBack = true;
        this.IsDirty = true;
        this.AppendChangeLog($"Copied from {sourceName}: " + string.Join(", ", tables.Select(t => t.Name)));
        await this.AfterBytesChangedAsync();
        this.Status = $"Copied {tables.Count} table(s) from {sourceName} into the open BIN (not saved yet). " +
                      "Use Save As to write it with corrected checksums, or undo the copy.";
    }

    public async Task UndoCopyBackAsync()
    {
        if (this.BinData == null || this.copyBackHistory.Undo(this.BinData) is not { } description)
        {
            return;
        }

        this.CanUndoCopyBack = this.copyBackHistory.Count > 0;
        this.IsDirty = true;
        this.AppendChangeLog("Copy undone: " + description);
        await this.AfterBytesChangedAsync();
        this.Status = $"Copy undone: {description} (not saved yet)." +
                      (this.CanUndoCopyBack ? $" {this.copyBackHistory.Count} earlier copy(ies) can still be undone." : string.Empty);
    }

    /// <summary>The BIN's bytes changed under the open page: show them again.</summary>
    private async Task AfterBytesChangedAsync()
    {
        if (this.ReferenceData != null)
        {
            await this.RecomputeChangesAsync();
        }

        this.ShowEditor(this.SelectedNode, true);
        this.ShowPatch(this.SelectedNode);
    }

    // ---- Patches ----

    [ObservableProperty] private PatchViewModel? _patch;

    private Dictionary<PatchDefinition, PatchViewModel> patchViewModels = new();

    private void ShowPatch(ParameterNode? node)
    {
        if (node?.Patch == null || this.BinData == null)
        {
            this.Patch = null;
            return;
        }

        if (!this.patchViewModels.TryGetValue(node.Patch, out PatchViewModel? vm))
        {
            vm = new PatchViewModel(node.Patch, this.BinData, () => this.ReferenceData, () =>
            {
                this.IsDirty = true;
                this.Status = "Patch changed: " + node.Patch.Name + " (not saved yet)";
                this.AppendChangeLog("Patch changed: " + node.Patch.Name);
            });
            this.patchViewModels[node.Patch] = vm;
        }
        else
        {
            vm.Refresh();
        }

        this.Patch = vm;
    }

    // ---- Pins, tags and notes ----

    private readonly TuneNotesStore store = new();

    // Notes live in the open project's folder; pins and tags stay in the app-wide store.
    private TuneNotesStore? projectNotes;

    private TuneNotesStore Notes => this.projectNotes ?? this.store;
    private bool rebuildingTree;
    private bool loadingPanel;

    public static IReadOnlyList<string> TagChoices { get; } = TuneNotesStore.AllTags;

    public static IReadOnlyList<string> TagFilterChoices { get; } =
        new[] { "All tags" }.Concat(TuneNotesStore.AllTags.Skip(1)).ToArray();

    partial void OnEditorChanged(ValueEditorViewModel? value)
    {
        if (value is not { Is2D: true })
        {
            this.ShowSurface = false;
        }
    }

    partial void OnTagFilterChanged(string value) => this.ApplyFilter(this.SearchText);

    partial void OnSelectedPinnedChanged(bool value)
    {
        if (this.loadingPanel || this.SelectedNode?.Table == null)
        {
            return;
        }

        string name = this.SelectedNode.Table.Name;
        this.store.SetPinned(name, value);
        this.ForTable(name, leaf => leaf.IsPinned = value);
        this.RebuildPinned(name);
    }

    partial void OnSelectedTagChanged(string value)
    {
        if (this.loadingPanel || this.SelectedNode?.Table == null)
        {
            return;
        }

        string name = this.SelectedNode.Table.Name;
        this.store.SetTag(name, value);
        this.ForTable(name, leaf => leaf.Tag = value == "(none)" ? string.Empty : value);
        if (this.TagFilter != "All tags")
        {
            this.ApplyFilter(this.SearchText);
        }
    }

    // The table whose note is shown in the box; typing saves to it, whatever is selected by then.
    private string? noteTable;

    partial void OnSelectedNoteChanged(string value)
    {
        if (this.loadingPanel || this.noteTable == null)
        {
            return;
        }

        string name = this.noteTable;
        this.Notes.SetNote(this.BinPath, name, value);
        bool has = !string.IsNullOrWhiteSpace(value);
        this.ForTable(name, leaf => leaf.HasNote = has);
    }

    private void RefreshPanel(ParameterNode? node)
    {
        this.loadingPanel = true;
        try
        {
            string? name = node?.Table?.Name;
            this.HasSelectedTable = name != null;
            this.SelectedPinned = name != null && this.store.IsPinned(name);
            string tag = name == null ? string.Empty : this.store.GetTag(name);
            this.SelectedTag = tag.Length == 0 ? "(none)" : tag;
            this.noteTable = name;
            this.SelectedNote = name == null ? string.Empty : this.Notes.GetNote(this.BinPath, name);
        }
        finally
        {
            this.loadingPanel = false;
        }
    }

    private void ForTable(string name, Action<ParameterNode> action)
    {
        foreach (ParameterNode leaf in this.AllLeaves())
        {
            if (leaf.Table != null && string.Equals(leaf.Table.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                action(leaf);
            }
        }
    }

    /// <summary>Copy stored pins, tags and notes onto the freshly built tree and add the Pinned folder.</summary>
    private void ApplyMeta()
    {
        foreach (ParameterNode leaf in this.AllLeaves().Where(l => l.Table != null))
        {
            string name = leaf.Table!.Name;
            leaf.IsPinned = this.store.IsPinned(name);
            leaf.Tag = this.store.GetTag(name);
            leaf.HasNote = this.Notes.GetNote(this.BinPath, name).Length > 0;
        }

        this.RebuildPinned(null);
    }

    private const string PinnedFolder = "⭐ Pinned";

    private void RebuildPinned(string? keepSelected)
    {
        this.allCategories.RemoveAll(c => c.Name == PinnedFolder);

        List<ParameterNode> pinned = this.AllLeaves()
            .Where(l => l.Table != null && l.IsPinned)
            .GroupBy(l => l.Table!.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(l => l.Name)
            .ToList();

        if (pinned.Count > 0)
        {
            ParameterNode folder = new() { Name = PinnedFolder, IsExpanded = true };
            foreach (ParameterNode source in pinned)
            {
                folder.Children.Add(new ParameterNode
                {
                    Name = source.Name,
                    Table = source.Table,
                    IsUnscaled = source.IsUnscaled,
                    IsPinned = true,
                    Tag = source.Tag,
                    HasNote = source.HasNote,
                    HasChanges = source.HasChanges,
                });
            }

            this.allCategories.Insert(0, folder);
        }

        string? selectedName = keepSelected ?? this.SelectedNode?.Table?.Name;
        this.rebuildingTree = true;
        try
        {
            this.ApplyFilter(this.SearchText);
        }
        finally
        {
            this.rebuildingTree = false;
        }

        // The tree was rebuilt; put the selection back on the same table.
        if (selectedName != null)
        {
            ParameterNode? again = this.allCategories
                .Where(c => c.Name != PinnedFolder)
                .SelectMany(c => this.LeavesOf(c))
                .FirstOrDefault(l => l.Table != null
                    && string.Equals(l.Table.Name, selectedName, StringComparison.OrdinalIgnoreCase));
            if (again != null)
            {
                this.SelectedNode = again;
            }
        }
    }

    private IEnumerable<ParameterNode> LeavesOf(ParameterNode root)
    {
        Stack<ParameterNode> pending = new();
        pending.Push(root);
        while (pending.Count > 0)
        {
            ParameterNode node = pending.Pop();
            if (node.IsCategory)
            {
                foreach (ParameterNode child in node.Children)
                {
                    pending.Push(child);
                }
            }
            else
            {
                yield return node;
            }
        }
    }

    private IEnumerable<ParameterNode> AllLeaves()
    {
        Stack<ParameterNode> pending = new(this.allCategories);
        while (pending.Count > 0)
        {
            ParameterNode node = pending.Pop();
            if (node.IsCategory)
            {
                foreach (ParameterNode child in node.Children)
                {
                    pending.Push(child);
                }
            }
            else
            {
                yield return node;
            }
        }
    }

    partial void OnSelectedNodeChanged(ParameterNode? value)
    {
        // While the tree is being rebuilt the TreeView briefly reports "nothing selected".
        if (this.rebuildingTree)
        {
            return;
        }

        this.Details = value?.TooltipText ?? "Select a parameter to see its details.";
        this.RefreshPanel(value);
        this.ShowPatch(value);

        // Same table as the open editor (for example the pinned copy of it): keep the editor and its undo history.
        if (value?.Table != null && ReferenceEquals(this.Editor?.Table, value.Table))
        {
            return;
        }

        this.ShowEditor(value);
    }

    /// <summary>
    /// Show a table's editor, opening a tab for it if it is not open yet. With fresh, the editors are rebuilt (the BIN's
    /// bytes or the comparison changed under them).
    /// </summary>
    private void ShowEditor(ParameterNode? node, bool fresh = false)
    {
        if (node?.Table == null || this.BinData == null)
        {
            this.ActiveTab = null;
            this.Editor = null;
            return;
        }

        if (fresh)
        {
            // Every open table reads the BIN or the reference again; the table on show gets a new editor below.
            foreach (EditorTab other in this.OpenTabs)
            {
                if (!ReferenceEquals(other.Table, node.Table))
                {
                    other.Editor = this.CreateEditor(other.Table);
                }
            }
        }

        EditorTab? tab = this.OpenTabs.FirstOrDefault(t => ReferenceEquals(t.Table, node.Table));
        if (tab == null)
        {
            tab = new EditorTab(node, this.CreateEditor(node.Table));
            this.OpenTabs.Add(tab);
        }
        else if (fresh)
        {
            tab.Editor = this.CreateEditor(node.Table);
        }

        // The editor first, so choosing the tab does not ask for the same table to be shown again.
        this.Editor = tab.Editor;
        this.ActiveTab = tab;

        // A reference to a table this XDF does not contain is evaluated as 1.0, which can be wrong by a large factor.
        // Say so instead of showing plausible-looking numbers.
        List<string> missing = System.Text.RegularExpressions.Regex
            .Matches(node.Table.ScalingEquation ?? string.Empty, @"(?<![A-Za-z0-9_])(?:ref|table)\s*:\s*'([^']*)'", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Select(m => m.Groups[1].Value)
            .Where(name => !this.tablesByName.ContainsKey(name))
            .Distinct()
            .ToList();
        if (missing.Count > 0)
        {
            this.Status = "Warning: this table's equation uses " + string.Join(", ", missing.Select(m => "'" + m + "'")) +
                          ", which is not in this XDF. The values shown may be wrong; do not edit this table.";
        }
    }

    private ValueEditorViewModel CreateEditor(TableDefinition table)
    {
        ValueEditorViewModel editor = new(table, this.BinData!, this.ResolveTableReference)
        {
            Reference = this.ReferenceData,
        };
        editor.Edited += record => this.LogEdit(editor, record);
        editor.DataChanged += () =>
        {
            this.IsDirty = true;
            this.Status = "Modified: " + editor.Name + " (not saved yet)";
            if (this.ReferenceData != null)
            {
                bool differs = editor.DiffersFromReference();
                this.ForTable(editor.Name, leaf => leaf.HasChanges = differs);
            }
        };
        return editor;
    }

    /// <summary>Equations can refer to another table's value, as ref:'name'.</summary>
    private double ResolveTableReference(string name) =>
        this.BinData != null && this.tablesByName.TryGetValue(name, out TableDefinition? t)
            ? t.GetScaledValue(this.BinData, 0, 0)
            : 1.0;

    /// <summary>Write the edited BIN to a new file, correcting its checksums first.</summary>
    public async Task SaveAsAsync(string path)
    {
        if (this.BinData == null)
        {
            return;
        }

        try
        {
            this.Status = "Correcting checksums...";

            // Work on a copy: the checksum correction and the file write run off the UI thread, and the live BIN may be edited meanwhile.
            byte[] live = this.BinData;
            byte[] original = (byte[])live.Clone();
            byte[] data = (byte[])live.Clone();
            ChecksumReport report = await Task.Run(() =>
            {
                ChecksumReport result = ChecksumService.CorrectAndValidate(data);
                File.WriteAllBytes(path, data);
                return result;
            });

            // Nothing changed while saving: the open BIN takes on the corrected checksums, as it always has.
            // Otherwise the file is saved without the late edit, and the BIN stays unsaved.
            bool editedMeanwhile = !live.AsSpan().SequenceEqual(original);
            if (!editedMeanwhile)
            {
                Array.Copy(data, live, data.Length);
            }

            if (this.BinPath != null)
            {
                this.Notes.CopyNotes(this.BinPath, path);
                this.AppendChangeLog("Saved as " + Path.GetFileName(path));
                this.CopyChangeLog(this.BinPath, path);
            }

            this.BinPath = path;
            this.BinName = Path.GetFileName(path);
            this.IsDirty = editedMeanwhile;

            // Only a file that PCMHammer's validator accepts is offered for flashing (and not one that lacks a late edit).
            this.ReadyToFlashPath = report.Valid && !editedMeanwhile ? path : null;
            this.Details = report.Details;
            this.Status = editedMeanwhile
                ? $"Saved as {this.BinName}, but you changed the BIN while it was saving, so that edit is not in the file. Save again."
                : report.Valid
                    ? $"Saved as {this.BinName}. {report.Summary}"
                    : $"Saved as {this.BinName}, but {report.Summary} Do not flash it until that is fixed.";
            this.ShowEditor(this.SelectedNode, true);
        }
        catch (Exception ex)
        {
            this.Status = "Save failed: " + ex.Message;
        }
    }

    /// <summary>Check the BIN as it is now, including unsaved edits, without changing it.</summary>
    public async Task CheckChecksumsAsync()
    {
        if (this.BinData == null)
        {
            this.Status = "Open a BIN first.";
            return;
        }

        byte[] copy = (byte[])this.BinData.Clone();
        ChecksumReport report = await Task.Run(() => ChecksumService.Validate(copy));
        this.Details = report.Details;
        this.Status = report.Summary + (report.Valid || !this.IsDirty ? string.Empty : " (your edits are not saved yet; Save As corrects them where it can)");
    }

    public void UndoEdit()
    {
        if (this.Editor != null && this.Editor.Undo())
        {
            this.Status = "Undid the last edit to " + this.Editor.Name;
        }
    }

    public void RedoEdit()
    {
        if (this.Editor != null && this.Editor.Redo())
        {
            this.Status = "Redid the last undone edit to " + this.Editor.Name;
        }
    }

    // ---- Change log: one line per edit, in a .changes file beside the BIN ----

    /// <summary>The change log for the open BIN (it may not exist yet).</summary>
    public string? ChangeLogPath =>
        this.CurrentProject != null ? this.CurrentProject.ChangeLogFile
        : this.BinPath == null ? null
        : Path.ChangeExtension(this.BinPath, ".changes");

    private void LogEdit(ValueEditorViewModel editor, EditRecord record)
    {
        if (record.Cells.Count == 0 && record.Kind != "Edit")
        {
            return;
        }

        System.Text.StringBuilder text = new();
        text.Append(editor.Name).Append(" | ").Append(record.Kind).Append(": ").Append(record.Label);
        if (record.Cells.Count > 0)
        {
            text.Append(" | ").Append(record.Cells.Count).Append(record.Cells.Count == 1 ? " cell" : " cells");
        }

        // Spell out the cells for small edits; a big block would drown the log.
        const int detailLimit = 24;
        foreach (CellChange cell in record.Cells.Take(detailLimit))
        {
            text.AppendLine().Append("    ")
                .Append(editor.RowLabel(cell.Row)).Append(" x ").Append(editor.ColumnLabel(cell.Column))
                .Append("   ").Append(editor.Table.FormatValue(cell.Old)).Append(" -> ").Append(editor.Table.FormatValue(cell.New));
        }

        if (record.Cells.Count > detailLimit)
        {
            text.AppendLine().Append("    ... and ").Append(record.Cells.Count - detailLimit).Append(" more");
        }

        this.AppendChangeLog(text.ToString());
    }

    private void AppendChangeLog(string text)
    {
        string? log = this.ChangeLogPath;
        if (log == null)
        {
            return;
        }

        try
        {
            // One log for the whole project, so each line says which BIN it was about.
            string bin = this.CurrentProject != null && this.BinPath != null ? Path.GetFileName(this.BinPath) + " | " : string.Empty;
            File.AppendAllText(log, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {bin}{text}{Environment.NewLine}");
        }
        catch
        {
            // A read-only folder must not stop editing; the log is a convenience.
        }
    }

    /// <summary>A BIN saved under a new name keeps its history.</summary>
    private void CopyChangeLog(string fromBin, string toBin)
    {
        if (this.CurrentProject != null)
        {
            return;   // a project keeps one log for all its BINs
        }

        try
        {
            string from = Path.ChangeExtension(fromBin, ".changes");
            string to = Path.ChangeExtension(toBin, ".changes");
            if (File.Exists(from) && !string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(from, to, true);
            }
        }
        catch
        {
            // Best effort.
        }
    }

    // ---- Notes export ----

    /// <summary>Write every note for the open BIN to a text file. Returns how many tables had notes.</summary>
    public async Task<int> ExportNotesAsync(string path)
    {
        IReadOnlyList<KeyValuePair<string, string>> notes = this.Notes.NotesFor(this.BinPath);
        System.Text.StringBuilder text = new();
        text.AppendLine("Tuning notes for " + this.BinName);
        text.AppendLine("Exported " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        text.AppendLine(new string('=', 60));
        text.AppendLine();
        foreach (KeyValuePair<string, string> note in notes)
        {
            text.AppendLine(note.Key);
            text.AppendLine(new string('-', Math.Min(60, note.Key.Length)));
            text.AppendLine(note.Value);
            text.AppendLine();
        }

        await File.WriteAllTextAsync(path, text.ToString());
        this.Status = notes.Count == 0
            ? "There are no notes for this BIN yet; wrote an empty file."
            : $"Exported {notes.Count} note(s) to {Path.GetFileName(path)}.";
        return notes.Count;
    }

    // ---- Recent files ----

    private readonly AppSettings settings = AppSettings.Load();

    public ObservableCollection<string> RecentBins { get; } = new();

    /// <summary>Folder the file pickers should start in: where the last BIN came from.</summary>
    public string? LastFolder =>
        this.BinPath != null ? Path.GetDirectoryName(this.BinPath)
        : this.CurrentProject != null ? this.ProjectBinFolder(this.CurrentProject)
        : this.settings.RecentBins.Count > 0 ? Path.GetDirectoryName(this.settings.RecentBins[0])
        : null;

    /// <summary>Where a project's BINs are most likely to be: saved tunes if there are any, otherwise reads.</summary>
    private string ProjectBinFolder(ProjectInfo project) =>
        Directory.Exists(project.EditsFolder) && Directory.GetFiles(project.EditsFolder, "*.bin").Length > 0
            ? project.EditsFolder
            : project.ReadsFolder;

    /// <summary>Where Save As should start: the project's saved-tunes folder, or null outside a project.</summary>
    public string? SaveFolder => this.CurrentProject?.EditsFolder;

    private void LoadRecent()
    {
        this.RecentBins.Clear();
        foreach (string path in this.settings.RecentBins.Where(File.Exists).Take(8))
        {
            this.RecentBins.Add(path);
        }
    }

    private void RememberRecent(string path)
    {
        this.settings.RecentBins.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        this.settings.RecentBins.Insert(0, path);
        if (this.settings.RecentBins.Count > 8)
        {
            this.settings.RecentBins.RemoveRange(8, this.settings.RecentBins.Count - 8);
        }

        this.settings.Save();
        this.LoadRecent();
    }

    public TuneViewModel()
    {
        this.LoadRecent();
        if (ProjectInfo.MigrateLegacyRoot())
        {
            this.Status = "Copied your LS-Studio projects and XDF library to " + ProjectInfo.DefaultProjectsRoot + " (the old folder was left as it was).";
        }
    }

    // ---- Projects: one folder per vehicle holding reads, saved tunes, logs, the change log and notes ----

    [ObservableProperty] private ProjectInfo? _currentProject;

    /// <summary>Raised when a project is opened or closed, so the shell can point the Logger at the project's log folder.</summary>
    public event Action<ProjectInfo?>? ProjectChanged;

    public bool HasProject => this.CurrentProject != null;

    public string ProjectTitle => this.CurrentProject == null ? "No project" : this.CurrentProject.DisplayName;

    /// <summary>Inside a project the file button adds a BIN to the project; outside it just opens one.</summary>
    public string OpenBinLabel => this.HasProject ? "Add BIN..." : "Open BIN...";

    partial void OnCurrentProjectChanged(ProjectInfo? value)
    {
        this.OnPropertyChanged(nameof(this.HasProject));
        this.OnPropertyChanged(nameof(this.ProjectTitle));
        this.OnPropertyChanged(nameof(this.OpenBinLabel));
        this.WatchReads(value);
    }

    /// <summary>Raised to open the Help tab at a topic ("tuner") and optional section ("tuner#grid").</summary>
    public event Action<string>? HelpRequested;

    public void RequestHelp(string topic) => this.HelpRequested?.Invoke(topic);

    /// <summary>Raised when the project was created for reading from the PCM: the shell takes you to the Flash tab.</summary>
    public event Action<ProjectInfo>? FlashReadRequested;

    public void RequestFlashRead(ProjectInfo project) => this.FlashReadRequested?.Invoke(project);

    /// <summary>Raised when a finished BIN appears in the project's reads folder (for example a read from the Flash tab).</summary>
    public event Action<string>? NewReadDetected;

    private FileSystemWatcher? readsWatcher;
    private readonly HashSet<string> knownReads = new(StringComparer.OrdinalIgnoreCase);

    private void WatchReads(ProjectInfo? project)
    {
        this.readsWatcher?.Dispose();
        this.readsWatcher = null;
        this.knownReads.Clear();
        if (project == null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(project.ReadsFolder);
            foreach (string file in Directory.GetFiles(project.ReadsFolder, "*.bin"))
            {
                this.knownReads.Add(file);
            }

            FileSystemWatcher watcher = new(project.ReadsFolder, "*.bin")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                EnableRaisingEvents = true,
            };
            watcher.Created += (_, e) => _ = this.ReadSettledAsync(project, e.FullPath);
            watcher.Renamed += (_, e) => _ = this.ReadSettledAsync(project, e.FullPath);
            this.readsWatcher = watcher;
        }
        catch
        {
            // No watching (for example a network folder): reads can still be added with Add BIN.
        }
    }

    /// <summary>A read takes a while to write: wait until its size stops changing, then announce it.</summary>
    private async Task ReadSettledAsync(ProjectInfo project, string path)
    {
        if (!this.knownReads.Add(path))
        {
            return;
        }

        long last = -1;
        int stable = 0;
        for (int i = 0; i < 120 && stable < 2; i++)
        {
            await Task.Delay(1000);
            try
            {
                long length = new FileInfo(path).Length;
                stable = length > 0 && length == last ? stable + 1 : 0;
                last = length;
            }
            catch
            {
                stable = 0;
            }
        }

        if (stable < 2 || !ReferenceEquals(project, this.CurrentProject))
        {
            return;
        }

        if (project.BaseBinName == null)
        {
            // The first read of a project is its untouched starting point.
            project.BaseBinName = Path.GetRelativePath(project.ProjectFolder, path);
            try { project.Save(); } catch { }
        }

        this.NewReadDetected?.Invoke(path);
    }

    /// <summary>
    /// Add a BIN to the open project: a file from elsewhere is copied into reads (the original is left alone) and the
    /// copy is opened. Without a project it simply opens the file.
    /// </summary>
    public async Task ImportBinAsync(string path, bool copyIntoProject = true)
    {
        // A file that lives inside another project belongs to that project: switch to it instead of copying the file.
        if (ProjectInfo.FindContaining(path) is { } owner
            && !string.Equals(owner.ProjectFolder, this.CurrentProject?.ProjectFolder, StringComparison.OrdinalIgnoreCase))
        {
            this.OpenProject(owner);
            await this.OpenBinAsync(path);
            this.Status = $"Switched to project '{owner.DisplayName}'. " + this.Status;
            return;
        }

        if (this.CurrentProject is not { } project
            || !copyIntoProject
            || AppPaths.IsInside(path, project.ProjectFolder))
        {
            await this.OpenBinAsync(path);
            return;
        }

        try
        {
            string dest = Path.Combine(project.ReadsFolder, Path.GetFileName(path));
            for (int n = 2; File.Exists(dest); n++)
            {
                dest = Path.Combine(project.ReadsFolder, $"{Path.GetFileNameWithoutExtension(path)}_{n}{Path.GetExtension(path)}");
            }

            this.knownReads.Add(dest);   // our own copy, not a new read from the PCM
            File.Copy(path, dest, false);
            if (project.BaseBinName == null)
            {
                project.BaseBinName = Path.GetRelativePath(project.ProjectFolder, dest);
                project.Save();
            }

            await this.OpenBinAsync(dest);
            this.Status = $"Copied {Path.GetFileName(path)} into the project's reads folder and opened the copy. " + this.Status;
        }
        catch (Exception ex)
        {
            this.Status = "Could not add the BIN to the project: " + ex.Message;
        }
    }

    /// <summary>Make the project's folder (named from its name, made unique if needed) and its project.json.</summary>
    public ProjectInfo CreateProject(ProjectInfo draft, string? startBin = null)
    {
        string baseName = ProjectInfo.SafeFolderName(draft.DisplayName);
        if (baseName.Length == 0)
        {
            baseName = "Project";
        }

        string folder = Path.Combine(ProjectInfo.DefaultProjectsRoot, baseName);
        for (int n = 2; Directory.Exists(folder); n++)
        {
            folder = Path.Combine(ProjectInfo.DefaultProjectsRoot, baseName + "_" + n);
        }

        draft.ProjectFolder = folder;
        draft.Created = DateTime.Now;
        draft.EnsureFolders();

        // A BIN to start from is copied into reads as the untouched base; the original is left where it was.
        if (startBin != null && File.Exists(startBin))
        {
            string copy = ProjectInfo.CopyIn(startBin, draft.ReadsFolder);
            draft.BaseBinName = Path.GetRelativePath(folder, copy);
            draft.LastBinName = draft.BaseBinName;
        }

        draft.Save();
        return draft;
    }

    /// <summary>Make an existing folder (for example one made by hand) into a project, leaving its files where they are.</summary>
    public ProjectInfo AdoptFolder(string folder)
    {
        ProjectInfo project = new()
        {
            Name = Path.GetFileName(folder.TrimEnd('\\', '/')).Replace('_', ' ').Trim(),
            ProjectFolder = folder,
        };
        project.EnsureFolders();
        project.Save();
        return project;
    }

    /// <summary>The file name Save As should suggest: the project's name and the next free version number.</summary>
    public string SuggestedSaveName()
    {
        string stem = Path.GetFileNameWithoutExtension(this.BinPath ?? "tune");
        stem = System.Text.RegularExpressions.Regex.Replace(stem, @"(_v\d+|_edit)+$", string.Empty, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (this.CurrentProject is not { } project)
        {
            return stem + "_edit.bin";
        }

        stem = ProjectInfo.SafeFolderName(project.DisplayName);
        int next = 1;
        try
        {
            foreach (string file in Directory.GetFiles(project.EditsFolder, "*.bin"))
            {
                System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(
                    Path.GetFileNameWithoutExtension(file),
                    "^" + System.Text.RegularExpressions.Regex.Escape(stem) + @"_v(\d+)$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (m.Success && int.TryParse(m.Groups[1].Value, out int version))
                {
                    next = Math.Max(next, version + 1);
                }
            }
        }
        catch
        {
            // The first version name is still a fine suggestion.
        }

        return $"{stem}_v{next}.bin";
    }

    public void OpenProject(ProjectInfo project)
    {
        project.EnsureFolders();   // projects made by LS-Studio have no logs folder yet
        this.CurrentProject = project;
        this.projectNotes = new TuneNotesStore(project.NotesFile, keyByFileName: true);
        int moved = this.ImportOldNotes(project);
        this.settings.LastProject = project.ProjectFolder;
        this.settings.Save();
        this.RefreshNotesAfterProjectChange();
        this.ProjectChanged?.Invoke(project);
        this.Status = $"Project '{project.DisplayName}' is open. Its reads, saved tunes, logs, change log and notes are in {project.ProjectFolder}." +
                      (moved > 0 ? $" Moved {moved} older note(s) into it." : string.Empty);
    }

    /// <summary>
    /// Once per project: copy notes written earlier (in LS-Studio, or in the app-wide store) for BINs that sit inside
    /// this project's folder into the project's own notes. Nothing is removed from the old places.
    /// </summary>
    private int ImportOldNotes(ProjectInfo project)
    {
        if (project.NotesImported || this.projectNotes == null)
        {
            return 0;
        }

        // LS-Studio's projects now live under the PcmSuite Projects folder, so its old paths are translated.
        string Map(string path) =>
            AppPaths.IsInside(path, ProjectInfo.LegacyProjectsRoot)
                ? ProjectInfo.DefaultProjectsRoot + path.Substring(ProjectInfo.LegacyProjectsRoot.Length)
                : path;

        int moved = this.projectNotes.AddNotesIfMissing(
            this.store.AllNotes().Concat(TuneNotesStore.ReadLsStudioNotes())
                .Select(n => (Bin: Map(n.Bin), n.Table, n.Text))
                .Where(n => AppPaths.IsInside(n.Bin, project.ProjectFolder)));
        try
        {
            project.NotesImported = true;
            project.Save();
        }
        catch
        {
            // It will simply try again next time (and add nothing, since the notes are there).
        }

        return moved;
    }

    /// <summary>Open a project and, if it has a BIN, open that too (the last one used, else the newest tune or read).</summary>
    public async Task OpenProjectAsync(ProjectInfo project)
    {
        this.OpenProject(project);
        if (project.FindBestBin() is { } bin)
        {
            string note = this.Status;
            await this.OpenBinAsync(bin);
            this.Status = $"Project '{project.DisplayName}': " + this.Status;
        }
        else
        {
            this.Status = $"Project '{project.DisplayName}' has no BIN yet. Read one from the PCM on the Flash tab (save it in {project.ReadsFolder}), or use Add BIN.";
        }
    }

    public void CloseProject()
    {
        this.CurrentProject = null;
        this.projectNotes = null;
        this.settings.LastProject = string.Empty;
        this.settings.Save();
        this.RefreshNotesAfterProjectChange();
        this.ProjectChanged?.Invoke(null);
        this.Status = "Project closed.";
    }

    /// <summary>Open the project that was open when the app last closed (does not open its BIN).</summary>
    public async Task RestoreLastProjectAsync(bool openBin = true)
    {
        if (this.settings.LastProject.Length > 0 && ProjectInfo.Load(this.settings.LastProject) is { } project)
        {
            if (openBin)
            {
                await this.OpenProjectAsync(project);
            }
            else
            {
                this.OpenProject(project);
            }
        }
    }

    private void RefreshNotesAfterProjectChange()
    {
        if (this.allCategories.Count > 0)
        {
            this.ApplyMeta();
            this.RefreshPanel(this.SelectedNode);
        }
    }

    /// <summary>Remember which BIN (and XDF) the project was last used with.</summary>
    private void NoteProjectFiles(string? binPath, string? xdfPath)
    {
        if (this.CurrentProject is not { } project)
        {
            return;
        }

        try
        {
            if (binPath != null && AppPaths.IsInside(binPath, project.ProjectFolder))
            {
                project.LastBinName = Path.GetRelativePath(project.ProjectFolder, binPath);
            }

            if (xdfPath != null)
            {
                project.XdfFileName = Path.GetFileName(xdfPath);
            }

            project.Save();
        }
        catch
        {
            // Not being able to update project.json must not stop the BIN from opening.
        }
    }

    /// <summary>
    /// Forget everything about the BIN that was open. Without this, a new BIN whose XDF is missing or unreadable
    /// would be shown in the header while the tree and editor still worked on the previous BIN's bytes.
    /// </summary>
    private void ResetLoadedState()
    {
        this.SelectedNode = null;
        this.CloseAllTabs();
        this.Editor = null;
        this.Patch = null;
        this.patchViewModels = new Dictionary<PatchDefinition, PatchViewModel>();
        this.allCategories = new List<ParameterNode>();
        this.Categories.Clear();
        this.tablesByName = new Dictionary<string, TableDefinition>(StringComparer.OrdinalIgnoreCase);
        this.BinData = null;
        this.StockBinPath = null;
        this.IsBinOnly = false;
        this.ReferenceData = null;
        this.ReferenceName = string.Empty;
        this.IsComparing = false;
        this.ChangedOnly = false;
        this.swapHistory.Clear();
        this.copyBackHistory.Clear();
        this.CanUndoCopyBack = false;
        this.CanUndoSwap = false;
        this.IsDirty = false;
        this.ReadyToFlashPath = null;
    }

    /// <summary>True while a matching BIN is known and nothing is being compared (the menu then offers "Compare with: <file>").</summary>
    public bool CanOfferStock => this.StockBinPath != null && !this.IsComparing;

    public string StockBinLabel => "Compare with: " + Path.GetFileName(this.StockBinPath);

    /// <summary>Compare the open BIN with the BIN that was found for it in the Reference BINs folder.</summary>
    public async Task CompareToStockAsync()
    {
        if (this.StockBinPath is { } path)
        {
            await this.CompareAsync(path);
        }
    }

    /// <summary>Look in the Reference BINs folder for a file with the same part numbers as the open BIN.</summary>
    private async Task FindStockBinAsync(string path)
    {
        string? match = null;
        try
        {
            match = await Task.Run(() => StockBins.FindMatch(File.ReadAllBytes(path), path, StockBins.Folder));
        }
        catch (Exception)
        {
            // No suggestion is not an error.
        }

        // The user may have opened something else while this ran.
        if (!string.Equals(this.BinPath, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        this.StockBinPath = match;
        if (match != null && !this.IsComparing)
        {
            this.Status += $" A BIN with the same part numbers is in your Reference BINs folder: {Path.GetFileName(match)} (menu, Checks, Compare with).";
        }
    }

    public async Task OpenBinAsync(string path)
    {
        await this.OpenBinCoreAsync(path);
        await this.FindStockBinAsync(path);
    }

    private async Task OpenBinCoreAsync(string path)
    {
        this.ResetLoadedState();
        this.RememberRecent(path);
        this.NoteProjectFiles(path, null);
        this.BinPath = path;
        this.BinName = Path.GetFileName(path);
        this.XdfPath = null;
        this.XdfName = "Searching...";
        this.Status = "BIN loaded: " + this.BinName + " - searching for a matching XDF...";

        IReadOnlyList<string> candidates = await DetectXdfsAsync(path, this.CurrentProject?.ProjectFolder);
        if (candidates.Count == 0)
        {
            this.XdfName = "None found";
            string ids = string.Empty;
            try
            {
                ids = string.Join(", ", OsIdDetector.ScanForOsIds(File.ReadAllBytes(path)).Take(6));
            }
            catch
            {
                // The message is still useful without the numbers.
            }

            this.Status = "No XDF found" + (ids.Length > 0 ? " for the part numbers in this BIN (" + ids + ")" : string.Empty) +
                          ". Looked for an XDF with one of those numbers in its file name, in the BIN's folder and in " +
                          ProjectInfo.XdfLibraryFolder + ". Use Open XDF, or put one there.";
            await this.LoadBinOnlyAsync();
            return;
        }

        // Some XDFs are protected and cannot be read: take the best one that can.
        foreach (string xdf in candidates)
        {
            this.XdfPath = xdf;
            this.XdfName = Path.GetFileName(xdf) + " (auto-detected)";
            if (await this.LoadAsync())
            {
                this.NoteProjectFiles(null, xdf);
                await this.CompareToProjectBaseAsync();
                return;
            }
        }

        this.XdfPath = null;
        this.XdfName = "None readable";
        this.Status = $"Found {candidates.Count} XDF(s) for this BIN ({string.Join(", ", candidates.Select(Path.GetFileName).Take(3))}) " +
                      "but none could be read. Protected XDFs (the author has scrambled the file) only open in TunerPro, and nothing outside it can read them. " +
                      "Use Open XDF with an unprotected copy, or put one in " + ProjectInfo.XdfLibraryFolder + ". The BIN is open without tables.";
        await this.LoadBinOnlyAsync();
    }

    /// <summary>
    /// Open the BIN with no XDF, so there are no tables but the hex view, checksum check and segment swap still work.
    /// Appends to the status message already set by the caller.
    /// </summary>
    private async Task LoadBinOnlyAsync()
    {
        if (this.BinPath == null)
        {
            return;
        }

        try
        {
            string path = this.BinPath;
            byte[] data = await Task.Run(() => File.ReadAllBytes(path));
            this.BinData = data;
            this.IsBinOnly = true;
            this.IsDirty = false;
            this.Status += " Meanwhile the hex view, checksum check and segment swap work on this BIN.";
        }
        catch (Exception ex)
        {
            this.Status += " (The BIN could not be read: " + ex.Message + ")";
        }
    }

    /// <summary>In a project, mark every difference from its untouched base BIN as soon as another BIN is open.</summary>
    private async Task CompareToProjectBaseAsync()
    {
        // Only BINs that belong to the project are compared with its base (a file opened "just to look" is not).
        if (this.CurrentProject?.BaseBinPath is not { } baseBin || this.BinPath == null || this.BinData == null
            || !AppPaths.IsInside(this.BinPath, this.CurrentProject.ProjectFolder)
            || string.Equals(Path.GetFullPath(baseBin), Path.GetFullPath(this.BinPath), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string before = this.Status;
        await this.CompareAsync(baseBin);
        if (!this.IsComparing)
        {
            this.Status = before;   // a different-sized base cannot be compared; say nothing more
        }
    }

    /// <summary>Forget a manually chosen XDF and let the BIN's OS ID pick one again.</summary>
    public async Task AutoDetectXdfAsync()
    {
        if (this.BinPath == null)
        {
            this.Status = "Open a BIN first.";
            return;
        }

        await this.OpenBinAsync(this.BinPath);
    }

    /// <summary>
    /// Keep the tables now on screen as the Tuner's own definition for this BIN's OS: an XDF in the shared library, named with the OS ID,
    /// so the next BIN with that OS finds it without any XDF being supplied.
    /// </summary>
    public async Task SaveMyDefinitionAsync()
    {
        if (this.BinData == null || this.IsBinOnly || this.tablesByName.Count == 0)
        {
            this.Status = "Nothing to save yet: open a BIN with an XDF first, then the tables on screen can be kept as your definition.";
            return;
        }

        try
        {
            List<TableDefinition> tables = this.tablesByName.Values.ToList();
            byte[] bin = this.BinData;
            string source = this.XdfName.Length > 0 ? Path.GetFileName(this.XdfPath ?? this.XdfName) : "the open BIN";
            string path = await Task.Run(() => DefinitionStore.Save(tables, bin, source, ProjectInfo.XdfLibraryFolder));
            this.Status = $"Saved {tables.Count} tables as {Path.GetFileName(path)} in the XDF library (patches are not included). " +
                          "It is offered automatically for any BIN with the same OS ID.";
        }
        catch (Exception ex)
        {
            this.Status = "The definition could not be saved: " + ex.Message;
        }
    }

    public async Task OpenXdfAsync(string path)
    {
        string? previousPath = this.XdfPath;
        string previousName = this.XdfName;
        this.XdfPath = path;
        this.XdfName = Path.GetFileName(path) + " (manually selected)";
        if (!await this.LoadAsync())
        {
            // Not readable (a protected TunerPro file, or not an XDF): keep what was open and say so.
            this.XdfPath = previousPath;
            this.XdfName = previousName;
            string reason = this.Status;
            this.Status = $"{Path.GetFileName(path)} was not opened. {reason} " +
                          "(A protected XDF is scrambled by its author, so only TunerPro can open it. Look for an unprotected copy of it.) " +
                          (previousPath != null ? "Still using " + previousName + "." : "Use an unprotected XDF.");
            return;
        }

        // Same as LS-Studio: keep one copy of each (readable) XDF in the shared library. The copy is of the file that was actually loaded
        // (when the XDF had to be converted, that is the converted copy, not the original), and a different XDF that is already in the
        // library under the same name is never overwritten.
        try
        {
            string loaded = this.XdfPath ?? path;
            Directory.CreateDirectory(ProjectInfo.XdfLibraryFolder);
            string library = Path.Combine(ProjectInfo.XdfLibraryFolder, Path.GetFileName(loaded));
            string note = string.Empty;
            if (!string.Equals(Path.GetFullPath(loaded), Path.GetFullPath(library), StringComparison.OrdinalIgnoreCase))
            {
                if (!File.Exists(library))
                {
                    await Task.Run(() => File.Copy(loaded, library));
                    note = $"{Path.GetFileName(loaded)} was added to the XDF library. ";
                }
                else if (!await Task.Run(() => SameContent(loaded, library)))
                {
                    note = "A different XDF with the same name is already in the XDF library, so this one was not copied there. ";
                    library = loaded;   // keep using the file that was chosen
                }
            }

            this.XdfPath = library;
            this.XdfName = Path.GetFileName(library) + " (manually selected)";
            this.NoteProjectFiles(null, library);
            if (note.Length > 0)
            {
                this.Status = note + this.Status;
            }
        }
        catch
        {
            // The XDF is loaded; failing to copy it into the library is not worth a message.
        }

        await this.CompareToProjectBaseAsync();
    }

    private static bool SameContent(string a, string b) =>
        new FileInfo(a).Length == new FileInfo(b).Length && File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));

    private async Task<bool> LoadAsync()
    {
        if (this.BinPath == null || this.XdfPath == null)
        {
            this.Status = "Open a BIN and an XDF to load parameters.";
            return false;
        }

        try
        {
            this.Status = "Parsing XDF...";
            string bin = this.BinPath;
            string xdf = this.XdfPath;

            // A BIN opened without an XDF may already have been changed (a segment swap): keep those edits when an XDF is added.
            byte[] data = this.IsBinOnly && this.BinData != null && this.IsDirty ? this.BinData : await Task.Run(() => File.ReadAllBytes(bin));

            List<TableDefinition> tables;
            try
            {
                tables = await Task.Run(() => XdfParser.Parse(xdf));
            }
            catch (System.Xml.XmlException)
            {
                // Protected or binary-wrapped TunerPro XDF: convert it to clean XML and retry.
                // (LS-Studio had a manual Convert XDF button for this.)
                this.Status = "Converting XDF - stripping protection and binary markers...";
                string clean = Path.Combine(
                    Path.GetDirectoryName(xdf)!, Path.GetFileNameWithoutExtension(xdf) + "_clean.xdf");
                string? error = await Task.Run(() => XdfParser.ConvertToCleanXdf(xdf, clean));
                if (error != null)
                {
                    this.Status = "This XDF could not be read or converted: " + error;
                    return false;
                }

                try
                {
                    tables = await Task.Run(() => XdfParser.Parse(clean));
                }
                catch (Exception)
                {
                    // Converting did not help: do not leave a useless copy in the user's XDF folder.
                    try { File.Delete(clean); } catch { }
                    this.XdfName = Path.GetFileName(xdf) + " (unreadable)";
                    this.Status = "This XDF cannot be read, even after conversion. Try another XDF with Open XDF.";
                    return false;
                }

                xdf = clean;
                this.XdfPath = clean;
                this.XdfName = Path.GetFileName(clean) + " (converted)";
            }

            List<PatchDefinition> patches = await Task.Run(() => XdfParser.ParsePatches(xdf));

            // Check before changing anything: an XDF with nothing in it must leave the open BIN, its edits and its tables as they were.
            if (tables.Count == 0 && patches.Count == 0)
            {
                this.Status = "XDF parsed but no tables found. Check that the XDF matches this BIN.";
                return false;
            }

            bool keepEdits = this.IsBinOnly && this.IsDirty;
            this.BinData = data;
            this.IsBinOnly = false;
            this.ReadyToFlashPath = null;
            if (!keepEdits)
            {
                this.swapHistory.Clear();
                this.copyBackHistory.Clear();
                this.CanUndoCopyBack = false;
                this.CanUndoSwap = false;
            }

            this.patchViewModels = new Dictionary<PatchDefinition, PatchViewModel>();
            this.Patch = null;
            this.ReferenceData = null;
            this.ReferenceName = string.Empty;
            this.IsComparing = false;
            this.ChangedOnly = false;
            this.IsDirty = keepEdits;
            this.SelectedNode = null;
            this.CloseAllTabs();
            this.Editor = null;
            this.tablesByName = new Dictionary<string, TableDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (TableDefinition table in tables)
            {
                this.tablesByName[table.Name] = table;
            }

            this.allCategories = BuildTree(tables, patches);
            this.ApplyMeta();
            this.ApplyFilter(this.SearchText);
            this.Status = $"Loaded {tables.Count} tables and {patches.Count} patches - {this.BinName}";
            return true;
        }
        catch (Exception ex)
        {
            this.Status = "Error: " + ex.Message;
            return false;
        }
    }

    private static Task<IReadOnlyList<string>> DetectXdfsAsync(string binPath, string? projectFolder) => Task.Run<IReadOnlyList<string>>(() =>
    {
        List<string> found = new();
        try
        {
            byte[] bin = File.ReadAllBytes(binPath);
            string?[] folders = { Path.GetDirectoryName(binPath), projectFolder, ProjectInfo.XdfLibraryFolder };
            foreach (string? folder in folders)
            {
                if (folder == null || !Directory.Exists(folder))
                {
                    continue;
                }

                foreach (string match in OsIdDetector.FindMatchingXdfs(bin, folder))
                {
                    // The same file name in a later folder is the same XDF.
                    if (!found.Any(f => string.Equals(Path.GetFileName(f), match, StringComparison.OrdinalIgnoreCase)))
                    {
                        found.Add(Path.Combine(folder, match));
                    }
                }
            }
        }
        catch
        {
            // Treated as "none found".
        }

        return found;
    });

    private static List<ParameterNode> BuildTree(List<TableDefinition> tables, List<PatchDefinition> patches)
    {
        Dictionary<string, ParameterNode> top = new(StringComparer.OrdinalIgnoreCase);

        ParameterNode GetTop(string category)
        {
            if (!top.TryGetValue(category, out ParameterNode? node))
            {
                node = new ParameterNode { Name = category };
                top[category] = node;
            }

            return node;
        }

        static ParameterNode GetSub(ParameterNode parent, string name)
        {
            ParameterNode? existing = parent.Children.FirstOrDefault(c =>
                c.IsCategory && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                return existing;
            }

            ParameterNode node = new() { Name = name };
            parent.Children.Add(node);
            return node;
        }

        foreach (TableDefinition t in tables)
        {
            string category = string.IsNullOrEmpty(t.Category) ? "Other" : t.Category;

            // Memory-segment labels (Seg-OS, Seg-EngineCal...) are flash bookkeeping, not tuning.
            if (category.StartsWith("Seg-", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ParameterNode parent = GetTop(category);
            ParameterNode leaf = new() { Name = t.Name, Table = t, IsUnscaled = IsIdentity(t.ScalingEquation) };
            if (!string.IsNullOrEmpty(t.SubCategory))
            {
                parent = GetSub(parent, t.SubCategory);
                if (!string.IsNullOrEmpty(t.SubSubCategory))
                {
                    parent = GetSub(parent, t.SubSubCategory);
                }
            }

            parent.Children.Add(leaf);
        }

        foreach (PatchDefinition p in patches)
        {
            ParameterNode parent = GetTop(string.IsNullOrEmpty(p.Category) ? "Patches" : p.Category);
            if (!string.IsNullOrEmpty(p.SubCategory))
            {
                parent = GetSub(parent, p.SubCategory);
            }

            parent.Children.Add(new ParameterNode { Name = p.Name, Patch = p });
        }

        // Each level: sub-folders first (alphabetical), then the leaves.
        static void Sort(ParameterNode node)
        {
            List<ParameterNode> folders = node.Children.Where(c => c.IsCategory).OrderBy(c => c.Name).ToList();
            List<ParameterNode> leaves = node.Children.Where(c => !c.IsCategory).ToList();
            node.Children.Clear();
            foreach (ParameterNode folder in folders)
            {
                Sort(folder);
                node.Children.Add(folder);
            }

            foreach (ParameterNode leaf in leaves)
            {
                node.Children.Add(leaf);
            }
        }

        List<ParameterNode> result = top.Values.OrderBy(n => n.Name).ToList();
        foreach (ParameterNode node in result)
        {
            Sort(node);
        }

        return result;
    }

    private void ApplyFilter(string text)
    {
        this.Categories.Clear();
        string tagFilter = this.TagFilter == "All tags" ? string.Empty : this.TagFilter;
        if (string.IsNullOrWhiteSpace(text) && !this.ChangedOnly && tagFilter.Length == 0)
        {
            foreach (ParameterNode node in this.allCategories)
            {
                this.Categories.Add(node);
            }

            return;
        }

        foreach (ParameterNode node in this.allCategories)
        {
            ParameterNode? kept = FilterNode(node, (text ?? string.Empty).Trim(), this.ChangedOnly, tagFilter);
            if (kept != null)
            {
                this.Categories.Add(kept);
            }
        }
    }

    /// <summary>Returns a copy of a folder holding only matching leaves, or null if nothing matches.</summary>
    private static ParameterNode? FilterNode(ParameterNode node, string text, bool changedOnly, string tag)
    {
        if (!node.IsCategory)
        {
            // A name match, or (for the DTC tables) a trouble code or description among the rows: "P0420" or "catalyst" finds the tables that list it.
            return (node.Name.Contains(text, StringComparison.OrdinalIgnoreCase) || (node.Table != null && DtcCatalog.TableMentions(node.Table, text)))
                   && (!changedOnly || node.HasChanges)
                   && (tag.Length == 0 || node.Tag == tag)
                ? node
                : null;
        }

        ParameterNode copy = new() { Name = node.Name, IsExpanded = true };
        foreach (ParameterNode child in node.Children)
        {
            ParameterNode? kept = FilterNode(child, text, changedOnly, tag);
            if (kept != null)
            {
                copy.Children.Add(kept);
            }
        }

        return copy.Children.Count > 0 ? copy : null;
    }

    private static bool IsIdentity(string? equation) =>
        string.IsNullOrWhiteSpace(equation) || equation == "X" || equation == "x";
}
