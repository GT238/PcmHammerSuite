using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PcmSuite.Tuning.Core.Models;

namespace PcmSuite.Tune;

public sealed class PatchEntryRow
{
    public string Name { get; init; } = string.Empty;

    public string Address { get; init; } = string.Empty;

    public string Size { get; init; } = string.Empty;

    public string Data { get; init; } = string.Empty;
}

/// <summary>
/// One binary patch from the XDF. Shows whether the loaded BIN already contains it, and applies or
/// takes it back in memory (nothing reaches disk until Save As).
/// </summary>
public sealed partial class PatchViewModel : ObservableObject
{
    private readonly PatchDefinition patch;
    private readonly byte[] bin;
    private readonly Func<byte[]?> reference;
    private readonly Action changed;
    private byte[][]? before;

    [ObservableProperty] private string _state = string.Empty;
    [ObservableProperty] private bool _canApply;
    [ObservableProperty] private bool _canRevert;

    public PatchViewModel(PatchDefinition patch, byte[] bin, Func<byte[]?> reference, Action changed)
    {
        this.patch = patch;
        this.bin = bin;
        this.reference = reference;
        this.changed = changed;

        foreach (PatchEntry e in patch.Entries)
        {
            this.Entries.Add(new PatchEntryRow
            {
                Name = e.Name,
                Address = $"0x{e.Address:X}",
                Size = $"{e.Data.Length} byte{(e.Data.Length == 1 ? string.Empty : "s")}",
                Data = BitConverter.ToString(e.Data).Replace("-", " "),
            });
        }

        this.Refresh();
    }

    public string Name => this.patch.Name;

    public string Description => this.patch.Description;

    public ObservableCollection<PatchEntryRow> Entries { get; } = new();

    /// <summary>Re-check the BIN, for example after the reference BIN changed.</summary>
    public void Refresh()
    {
        int matching = 0;
        int outOfRange = 0;
        foreach (PatchEntry e in this.patch.Entries)
        {
            if (e.Address + e.Data.Length > this.bin.Length)
            {
                outOfRange++;
            }
            else if (this.bin.AsSpan((int)e.Address, e.Data.Length).SequenceEqual(e.Data))
            {
                matching++;
            }
        }

        int total = this.patch.Entries.Count;
        if (outOfRange > 0)
        {
            this.State = $"Cannot apply: {outOfRange} of {total} entries lie outside this BIN.";
            this.CanApply = false;
            this.CanRevert = false;
            return;
        }

        this.State = matching == total
            ? "Applied: this BIN already contains the patch."
            : matching == 0
                ? "Not applied."
                : $"Partly applied: {matching} of {total} entries match.";
        this.CanApply = matching != total;
        this.CanRevert = matching > 0 && (this.before != null || this.reference() != null);
    }

    public void Apply()
    {
        // Remember what was there, so the patch can be taken back.
        this.before = this.patch.Entries.Select(e => this.bin.AsSpan((int)e.Address, e.Data.Length).ToArray()).ToArray();
        foreach (PatchEntry e in this.patch.Entries)
        {
            e.Data.CopyTo(this.bin, (int)e.Address);
        }

        this.Refresh();
        this.changed();
    }

    public void Revert()
    {
        byte[]? source = this.reference();
        for (int i = 0; i < this.patch.Entries.Count; i++)
        {
            PatchEntry e = this.patch.Entries[i];
            byte[]? original = this.before?[i];
            if (original == null && source != null && e.Address + e.Data.Length <= source.Length)
            {
                original = source.AsSpan((int)e.Address, e.Data.Length).ToArray();
            }

            original?.CopyTo(this.bin, (int)e.Address);
        }

        this.before = null;
        this.Refresh();
        this.changed();
    }
}
