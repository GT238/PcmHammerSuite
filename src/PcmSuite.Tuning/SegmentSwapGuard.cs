using System.Text;
using PcmSuite.Tuning.Core.Models;
using PcmSuite.Tuning.Core.Parsers;

namespace PcmSuite.Tune;

/// <summary>Do the open BIN and the donor run the same operating system?</summary>
public enum OsMatch
{
    Same,
    Different,
    Unknown,
}

/// <summary>What to tell the person before a swap goes ahead. <see cref="Danger"/> is for a likely brick, not just a caution.</summary>
public sealed record SwapReview(bool Danger, string Title, string Message);

/// <summary>
/// Guardrails for copying segments between BINs. A calibration is written for one operating system: put it into a
/// BIN that runs another and the PCM can fail to start after flashing. So the two OS IDs are compared, and a swap
/// between different (or unidentifiable) ones is never silent.
/// </summary>
public static class SegmentSwapGuard
{
    /// <summary>
    /// The operating system's part number. A BIN carries several part numbers (the calibration segments each have their own),
    /// so the numbers inside the calibration segments are left out and the last of the rest is the OS.
    /// </summary>
    public static string? OperatingSystemId(byte[] bin)
    {
        byte[] outside = (byte[])bin.Clone();
        foreach (SegmentInfo segment in ChecksumCalculator.ReadSegments(bin, requireP01Signature: false))
        {
            // Entry 0 spans the whole image; only the real calibration segments are blanked.
            if (segment.Index == 0 || segment.EndAddress >= (uint)outside.Length)
            {
                continue;
            }

            Array.Clear(outside, (int)segment.StartAddress, (int)segment.ByteCount);
        }

        return OsIdDetector.ScanForOsIds(outside).LastOrDefault();
    }

    public static OsMatch Compare(string? target, string? donor)
    {
        if (string.IsNullOrEmpty(target) || string.IsNullOrEmpty(donor))
        {
            return OsMatch.Unknown;
        }

        return string.Equals(target, donor, StringComparison.OrdinalIgnoreCase) ? OsMatch.Same : OsMatch.Different;
    }

    /// <summary>One line for the top of the swap page.</summary>
    public static string Verdict(OsMatch match, string? target, string? donor) => match switch
    {
        OsMatch.Same => $"Same operating system ({target}). Calibration segments are compatible.",
        OsMatch.Different => $"DIFFERENT operating systems: open BIN {target}, donor {donor}. Copying calibration segments between them can leave the PCM unable to start.",
        _ => "The operating system could not be identified in one or both BINs, so compatibility cannot be confirmed.",
    };

    /// <summary>
    /// The questions to ask before copying <paramref name="picked"/>, or null when nothing needs asking
    /// (same operating system and no segment that holds the vehicle's identity).
    /// </summary>
    public static SwapReview? Review(OsMatch match, string? target, string? donor, IReadOnlyList<SegmentInfo> picked)
    {
        List<string> risky = picked.Where(s => s.IsRisky).Select(s => s.Name).ToList();
        if (match == OsMatch.Same && risky.Count == 0)
        {
            return null;
        }

        StringBuilder text = new();
        bool danger = match == OsMatch.Different;
        string title;
        if (danger)
        {
            title = "Different operating systems";
            text.Append($"The open BIN runs OS {target} and the donor runs OS {donor}.\n\n")
                .Append("You are copying: ").Append(string.Join(", ", picked.Select(s => s.Name))).Append(".\n\n")
                .Append("A calibration written for one operating system can stop the PCM from starting when it is flashed into another. ")
                .Append("Only continue if you are certain the two are compatible and you have a way to recover the PCM.");
        }
        else if (match == OsMatch.Unknown)
        {
            title = "Compatibility cannot be confirmed";
            text.Append("The operating system could not be identified in one or both BINs, so there is no way to tell whether the donor's segments fit this PCM.\n\n")
                .Append("You are copying: ").Append(string.Join(", ", picked.Select(s => s.Name))).Append(".\n\n")
                .Append("Only continue if you know the donor is the matching file for this PCM.");
            danger = false;
        }
        else
        {
            title = "Risky segment selected";
        }

        if (risky.Count > 0)
        {
            text.Append("\n\nYou also ticked: ").Append(string.Join(", ", risky))
                .Append(". It holds the vehicle's identity and security data (VIN, system settings).");
        }

        return new SwapReview(danger, title, text.ToString());
    }
}
