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
    [ObservableProperty] private bool _isComparing;
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

    public string? BinPath { get; private set; }

    public string? XdfPath { get; private set; }

    public byte[]? BinData { get; private set; }

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
            this.ShowEditor(this.SelectedNode);
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

        this.ShowEditor(this.SelectedNode);
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

    // What the swap overwrote: only those address ranges are put back by Undo, so edits made elsewhere survive.
    private List<(uint Start, byte[] Bytes)>? swapBackup;

    /// <summary>Copy the chosen segments from a donor BIN into the open BIN, in memory.</summary>
    public async Task ApplySegmentsAsync(byte[] donor, IReadOnlyList<PcmSuite.Tuning.Core.Models.SegmentInfo> segments, string donorName)
    {
        if (this.BinData == null || segments.Count == 0)
        {
            return;
        }

        this.swapBackup = new List<(uint, byte[])>();
        foreach (PcmSuite.Tuning.Core.Models.SegmentInfo s in segments)
        {
            byte[] before = new byte[s.ByteCount];
            Array.Copy(this.BinData, s.StartAddress, before, 0, s.ByteCount);
            this.swapBackup.Add((s.StartAddress, before));
            Array.Copy(donor, s.StartAddress, this.BinData, s.StartAddress, s.ByteCount);
        }

        this.CanUndoSwap = true;
        this.IsDirty = true;
        await this.AfterBytesChangedAsync();
        this.Status = $"Copied {segments.Count} segment(s) from {donorName} into the open BIN (not saved yet). " +
                      "Use Save As to write it with corrected checksums, or Undo segment swap.";
    }

    public async Task UndoSwapAsync()
    {
        if (this.BinData == null || this.swapBackup == null)
        {
            return;
        }

        foreach ((uint start, byte[] bytes) in this.swapBackup)
        {
            Array.Copy(bytes, 0, this.BinData, start, bytes.Length);
        }

        this.swapBackup = null;
        this.CanUndoSwap = false;

        // The BIN differs from whatever was saved last, so it counts as modified (and is no longer "ready to flash").
        this.IsDirty = true;
        await this.AfterBytesChangedAsync();
        this.Status = "Segment swap undone (not saved yet).";
    }

    /// <summary>The BIN's bytes changed under the open page: show them again.</summary>
    private async Task AfterBytesChangedAsync()
    {
        if (this.ReferenceData != null)
        {
            await this.RecomputeChangesAsync();
        }

        this.ShowEditor(this.SelectedNode);
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
        this.store.SetNote(this.BinPath, name, value);
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
            this.SelectedNote = name == null ? string.Empty : this.store.GetNote(this.BinPath, name);
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
            leaf.HasNote = this.store.GetNote(this.BinPath, name).Length > 0;
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

    private void ShowEditor(ParameterNode? node)
    {
        if (node?.Table == null || this.BinData == null)
        {
            this.Editor = null;
            return;
        }

        ValueEditorViewModel editor = new(node.Table, this.BinData, this.ResolveTableReference)
        {
            Reference = this.ReferenceData,
        };
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
        this.Editor = editor;

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
            byte[] data = this.BinData;
            ChecksumReport report = await Task.Run(() =>
            {
                ChecksumReport result = ChecksumService.CorrectAndValidate(data);
                File.WriteAllBytes(path, data);
                return result;
            });

            if (this.BinPath != null)
            {
                this.store.CopyNotes(this.BinPath, path);
            }

            this.BinPath = path;
            this.BinName = Path.GetFileName(path);
            this.IsDirty = false;

            // Only a file that PCMHammer's validator accepts is offered for flashing.
            this.ReadyToFlashPath = report.Valid ? path : null;
            this.Details = report.Details;
            this.Status = report.Valid
                ? $"Saved as {this.BinName}. {report.Summary}"
                : $"Saved as {this.BinName}, but {report.Summary} Do not flash it until that is fixed.";
            this.ShowEditor(this.SelectedNode);
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

    // ---- Recent files ----

    private readonly AppSettings settings = AppSettings.Load();

    public ObservableCollection<string> RecentBins { get; } = new();

    /// <summary>Folder the file pickers should start in: where the last BIN came from.</summary>
    public string? LastFolder =>
        this.BinPath != null ? Path.GetDirectoryName(this.BinPath)
        : this.settings.RecentBins.Count > 0 ? Path.GetDirectoryName(this.settings.RecentBins[0])
        : null;

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
    }

    /// <summary>
    /// Forget everything about the BIN that was open. Without this, a new BIN whose XDF is missing or unreadable
    /// would be shown in the header while the tree and editor still worked on the previous BIN's bytes.
    /// </summary>
    private void ResetLoadedState()
    {
        this.SelectedNode = null;
        this.Editor = null;
        this.Patch = null;
        this.patchViewModels = new Dictionary<PatchDefinition, PatchViewModel>();
        this.allCategories = new List<ParameterNode>();
        this.Categories.Clear();
        this.tablesByName = new Dictionary<string, TableDefinition>(StringComparer.OrdinalIgnoreCase);
        this.BinData = null;
        this.ReferenceData = null;
        this.ReferenceName = string.Empty;
        this.IsComparing = false;
        this.ChangedOnly = false;
        this.swapBackup = null;
        this.CanUndoSwap = false;
        this.IsDirty = false;
        this.ReadyToFlashPath = null;
    }

    public async Task OpenBinAsync(string path)
    {
        this.ResetLoadedState();
        this.RememberRecent(path);
        this.BinPath = path;
        this.BinName = Path.GetFileName(path);
        this.XdfPath = null;
        this.XdfName = "Searching...";
        this.Status = "BIN loaded: " + this.BinName + " - searching for a matching XDF...";

        string? xdf = await DetectXdfAsync(path);
        if (xdf == null)
        {
            this.XdfName = "None found";
            this.Status = "No matching XDF found. Use Open XDF, or add XDF files to: " + ProjectInfo.XdfLibraryFolder;
            return;
        }

        this.XdfPath = xdf;
        this.XdfName = Path.GetFileName(xdf) + " (auto-detected)";
        await this.LoadAsync();
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

    public async Task OpenXdfAsync(string path)
    {
        // Same as LS-Studio: keep one copy of each XDF in the shared library.
        Directory.CreateDirectory(ProjectInfo.XdfLibraryFolder);
        string library = Path.Combine(ProjectInfo.XdfLibraryFolder, Path.GetFileName(path));
        if (!string.Equals(path, library, StringComparison.OrdinalIgnoreCase))
        {
            await Task.Run(() => File.Copy(path, library, overwrite: true));
        }

        this.XdfPath = library;
        this.XdfName = Path.GetFileName(library) + " (manually selected)";
        await this.LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (this.BinPath == null || this.XdfPath == null)
        {
            this.Status = "Open a BIN and an XDF to load parameters.";
            return;
        }

        try
        {
            this.Status = "Parsing XDF...";
            string bin = this.BinPath;
            string xdf = this.XdfPath;

            byte[] data = await Task.Run(() => File.ReadAllBytes(bin));

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
                    return;
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
                    return;
                }

                xdf = clean;
                this.XdfPath = clean;
                this.XdfName = Path.GetFileName(clean) + " (converted)";
            }

            List<PatchDefinition> patches = await Task.Run(() => XdfParser.ParsePatches(xdf));

            this.BinData = data;
            this.ReadyToFlashPath = null;
            this.swapBackup = null;
            this.CanUndoSwap = false;
            this.patchViewModels = new Dictionary<PatchDefinition, PatchViewModel>();
            this.Patch = null;
            this.ReferenceData = null;
            this.ReferenceName = string.Empty;
            this.IsComparing = false;
            this.ChangedOnly = false;
            this.IsDirty = false;
            this.SelectedNode = null;
            this.tablesByName = new Dictionary<string, TableDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (TableDefinition table in tables)
            {
                this.tablesByName[table.Name] = table;
            }

            if (tables.Count == 0 && patches.Count == 0)
            {
                this.Categories.Clear();
                this.allCategories = new List<ParameterNode>();
                this.Status = "XDF parsed but no tables found. Check that the XDF matches this BIN.";
                return;
            }

            this.allCategories = BuildTree(tables, patches);
            this.ApplyMeta();
            this.ApplyFilter(this.SearchText);
            this.Status = $"Loaded {tables.Count} tables and {patches.Count} patches - {this.BinName}";
        }
        catch (Exception ex)
        {
            this.Status = "Error: " + ex.Message;
        }
    }

    private static Task<string?> DetectXdfAsync(string binPath) => Task.Run(() =>
    {
        try
        {
            byte[] bin = File.ReadAllBytes(binPath);
            string?[] folders = { Path.GetDirectoryName(binPath), ProjectInfo.XdfLibraryFolder };
            foreach (string? folder in folders)
            {
                if (folder == null || !Directory.Exists(folder))
                {
                    continue;
                }

                string? match = OsIdDetector.FindMatchingXdf(bin, folder);
                if (match != null)
                {
                    return Path.Combine(folder, match);
                }
            }
        }
        catch
        {
            // Treated as "no match".
        }

        return (string?)null;
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
            return node.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
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
