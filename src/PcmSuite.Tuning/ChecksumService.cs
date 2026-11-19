using System.Text;
using PcmHacking;
using PcmSuite.Tuning.Core.Models;

namespace PcmSuite.Tune;

/// <summary>What the checks said about an image.</summary>
public sealed record ChecksumReport(bool Valid, bool Corrected, string Summary, string Details);

/// <summary>
/// Checks and corrects the checksums of a BIN. PCMHammer's own FileValidator identifies the image and
/// does the checking, so this agrees with what PCMHammer will accept at flash time. The fixing is
/// ours, because PCMHammer only validates.
/// </summary>
public static class ChecksumService
{
    /// <summary>Check an image without changing it.</summary>
    public static ChecksumReport Validate(byte[] image)
    {
        CaptureLogger log = new();
        PcmType type = new FileValidator(image, new CaptureLogger()).GetFileType();
        bool valid = new FileValidator(image, log).IdentifyAndValidate();

        string summary = valid
            ? $"All checksums are valid ({type})."
            : type == PcmType.Undefined
                ? "PCMHammer does not recognise this image."
                : $"Checksums are NOT valid for this {type} image.";
        return new ChecksumReport(valid, false, summary, log.Text);
    }

    /// <summary>
    /// Correct what can be corrected in place (P01 and P59, any size), then check with PCMHammer's validator.
    /// </summary>
    public static ChecksumReport CorrectAndValidate(byte[] image)
    {
        PcmType type = new FileValidator(image, new CaptureLogger()).GetFileType();

        int corrected = 0;
        bool attempted = false;
        if (type is PcmType.P01 or PcmType.P59)
        {
            attempted = true;
            corrected = ChecksumCalculator.CorrectSegmentTable(image).Count(m => m.Contains("fixed"));
        }

        ChecksumReport check = Validate(image);
        string action = attempted
            ? (corrected == 0 ? "No change needed. " : $"{corrected} segment checksum(s) corrected. ")
            : $"No automatic correction exists here for {type}. ";
        return check with { Corrected = corrected > 0, Summary = action + check.Summary };
    }

    private sealed class CaptureLogger : ILogger
    {
        private readonly StringBuilder text = new();

        public string Text => this.text.ToString();

        public void AddUserMessage(string message) => this.text.AppendLine(message);

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
