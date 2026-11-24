using CommunityToolkit.Mvvm.ComponentModel;
using PcmSuite.Tuning.Core.Models;

namespace PcmSuite.Tune;

/// <summary>One table kept open in the Tuner, with its own editor so its undo history survives switching to another table.</summary>
public sealed partial class EditorTab : ObservableObject
{
    [ObservableProperty] private ValueEditorViewModel _editor;

    public EditorTab(ParameterNode node, ValueEditorViewModel editor)
    {
        this.Node = node;
        this.Table = node.Table!;
        this._editor = editor;
    }

    /// <summary>The tree entry that opened it (used to put the selection back when the tab is chosen).</summary>
    public ParameterNode Node { get; internal set; }

    public TableDefinition Table { get; }

    public string Title => this.Table.Name;
}
