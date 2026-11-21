using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PcmHacking;
using PcmSuite.Tuning.Core.Models;
using PcmSuite.Tuning.Core.Parsers;

namespace PcmSuite.Tune;

/// <summary>
/// Copy whole calibration segments from a donor BIN into the BIN that is open in the Tune tab.
/// Nothing is written to disk here: the copy goes into the open BIN (as an edit), and Save As then
/// corrects the checksums like for any other edit.
/// </summary>
public sealed partial class SegmentSwapViewModel : ObservableObject
{
    private readonly byte[] target;
    private byte[]? donor;

    [ObservableProperty] private string _donorName = "No donor BIN chosen";
    [ObservableProperty] private string _status = "Choose the BIN to copy segments from.";
    [ObservableProperty] private string _preview = string.Empty;
    [ObservableProperty] private bool _hasSegments;
    [ObservableProperty] private string _osLine = string.Empty;
    [ObservableProperty] private string _osVerdict = string.Empty;
    [ObservableProperty] private OsMatch _osMatch = OsMatch.Unknown;

    public SegmentSwapViewModel(string targetName, byte[] target)
    {
        this.TargetName = targetName;
        this.target = target;
    }

    public string TargetName { get; }

    public ObservableCollection<SegmentInfo> Segments { get; } = new();

    /// <summary>The segments ticked for copying.</summary>
    public IReadOnlyList<SegmentInfo> Selected => this.Segments.Where(s => s.IsSelected && s.CanSwap).ToList();

    public byte[]? Donor => this.donor;

    private string? targetOs;
    private string? donorOs;

    /// <summary>What to ask before copying the ticked segments, or null when nothing needs asking.</summary>
    public SwapReview? Review() => SegmentSwapGuard.Review(this.OsMatch, this.targetOs, this.donorOs, this.Selected);

    public async Task LoadDonorAsync(string path)
    {
        try
        {
            byte[] data = await Task.Run(() => File.ReadAllBytes(path));
            this.donor = null;
            this.Segments.Clear();
            this.HasSegments = false;
            this.Preview = string.Empty;
            this.OsLine = string.Empty;
            this.OsVerdict = string.Empty;
            this.OsMatch = OsMatch.Unknown;

            if (data.Length != this.target.Length)
            {
                this.Status = $"The donor is {data.Length:N0} bytes and the open BIN is {this.target.Length:N0}. Segments can only be copied between BINs of the same size.";
                return;
            }

            PcmType donorType = new FileValidator(data, new SilentLogger()).GetFileType();
            PcmType targetType = new FileValidator(this.target, new SilentLogger()).GetFileType();
            if (donorType != targetType || targetType is not (PcmType.P01 or PcmType.P59))
            {
                this.Status = $"Segment copying is only supported between two P01 or P59 BINs of the same type (open BIN: {targetType}, donor: {donorType}).";
                return;
            }

            IReadOnlyList<SegmentInfo> mine = ChecksumCalculator.ReadSegments(this.target, requireP01Signature: false);
            IReadOnlyList<SegmentInfo> theirs = ChecksumCalculator.ReadSegments(data, requireP01Signature: false);
            if (mine.Count == 0)
            {
                this.Status = "Could not read the segment table of the open BIN.";
                return;
            }

            this.donor = data;
            this.DonorName = Path.GetFileName(path);
            this.targetOs = SegmentSwapGuard.OperatingSystemId(this.target);
            this.donorOs = SegmentSwapGuard.OperatingSystemId(data);
            this.OsMatch = SegmentSwapGuard.Compare(this.targetOs, this.donorOs);
            this.OsLine = "Open BIN OS: " + (this.targetOs ?? "not found") + "     Donor OS: " + (this.donorOs ?? "not found");
            this.OsVerdict = SegmentSwapGuard.Verdict(this.OsMatch, this.targetOs, this.donorOs);

            foreach (SegmentInfo t in mine)
            {
                SegmentInfo? d = theirs.FirstOrDefault(x => x.Index == t.Index);
                // Entry 0 of the table is the checksum of the whole image (it spans every other segment),
                // so copying it would replace the entire BIN. It is listed but cannot be ticked.
                bool wholeImage = t.StartAddress == 0 && t.EndAddress + 3 >= this.target.Length;
                bool sameRange = !wholeImage && d != null && d.StartAddress == t.StartAddress && d.EndAddress == t.EndAddress;
                bool different = sameRange && !data.AsSpan((int)t.StartAddress, (int)t.ByteCount)
                    .SequenceEqual(this.target.AsSpan((int)t.StartAddress, (int)t.ByteCount));

                SegmentInfo row = new()
                {
                    Index = t.Index,
                    Name = t.Name,
                    Description = t.Description,
                    StartAddress = t.StartAddress,
                    EndAddress = t.EndAddress,
                    ChecksumAddress = t.ChecksumAddress,
                    TargetChecksum = t.TargetChecksum,
                    DonorChecksum = d?.TargetChecksum ?? "-",
                    IsDifferent = different,
                    CanSwap = sameRange,
                    IsRisky = t.Index is 6,
                    Note = wholeImage
                        ? "Checksum of the whole image. It spans all the other segments, so it cannot be copied on its own."
                        : !sameRange
                        ? "Not available: this segment covers different addresses in the donor."
                        : !different
                            ? "Identical in both BINs."
                            : t.Index is 6
                                    ? "Holds the VIN, security and system settings. Copying it changes the vehicle's identity data."
                                    : "Differs between the two BINs.",
                };
                row.PropertyChanged += (_, _) => this.UpdatePreview();
                this.Segments.Add(row);
            }

            this.HasSegments = true;
            int diffCount = this.Segments.Count(s => s.IsDifferent);
            this.Status = this.OsMatch == OsMatch.Different
                ? $"{diffCount} segment(s) differ, but the operating systems do not match. Copying will ask you to confirm."
                : $"{diffCount} segment(s) differ. Tick the ones to copy from the donor into the open BIN.";
            this.UpdatePreview();
        }
        catch (Exception ex)
        {
            this.Status = "Could not read the donor: " + ex.Message;
        }
    }

    public void SelectDifferent()
    {
        foreach (SegmentInfo s in this.Segments)
        {
            // Risky ones are never ticked automatically.
            s.IsSelected = s.IsDifferent && s.CanSwap && !s.IsRisky;
        }
    }

    public void SelectNone()
    {
        foreach (SegmentInfo s in this.Segments)
        {
            s.IsSelected = false;
        }
    }

    private void UpdatePreview()
    {
        IReadOnlyList<SegmentInfo> picked = this.Selected;
        if (picked.Count == 0 || this.donor == null)
        {
            this.Preview = string.Empty;
            return;
        }

        long changed = 0;
        foreach (SegmentInfo s in picked)
        {
            for (uint i = s.StartAddress; i <= s.EndAddress; i++)
            {
                if (this.target[i] != this.donor[i])
                {
                    changed++;
                }
            }
        }

        this.Preview = $"{picked.Count} segment(s) selected, {changed:N0} bytes will change. " +
                       "They are copied into the open BIN as an edit; use Save As to write a file with corrected checksums.";
    }

    private sealed class SilentLogger : ILogger
    {
        public void AddUserMessage(string message)
        {
        }

        public void AddDebugMessage(string message)
        {
        }

        public void StatusUpdateActivity(string activity)
        {
        }

        public void StatusUpdateTimeRemaining(string remaining)
        {
        }

        public void StatusUpdatePercentDone(string percent)
        {
        }

        public void StatusUpdateRetryCount(string retries)
        {
        }

        public void StatusUpdateProgressBar(double completed, bool visible)
        {
        }

        public void StatusUpdateKbps(string kbps)
        {
        }

        public void StatusUpdateReset()
        {
        }

        public void StatusUpdateReset(string unused)
        {
        }
    }
}
