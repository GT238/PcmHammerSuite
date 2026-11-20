using System.Globalization;
using PcmSuite.Tune;

namespace PcmSuite;

/// <summary>
/// Hands the Logger's live readings to the Tune tab. It only reads the Logger's public Readings list (the same one its
/// Live Values screen shows), so PCM Hammer's code stays unchanged.
/// </summary>
internal sealed class LoggerLiveSource : ILiveSource
{
    private readonly PCMLoggerAvalonia.ViewModels.MainWindowViewModel logger;

    public LoggerLiveSource(PCMLoggerAvalonia.ViewModels.MainWindowViewModel logger) => this.logger = logger;

    public bool IsLive => this.logger.IsConnected && this.logger.IsLogging;

    public IReadOnlyList<LiveValue> Current()
    {
        List<LiveValue> values = new();
        foreach (PCMLoggerAvalonia.ViewModels.ReadingRow row in this.logger.Readings.ToArray())
        {
            // The values are the text the Logger shows; read them back as numbers (skip "--" and the like).
            string text = (row.Value ?? string.Empty).Trim();
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
                || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out number))
            {
                values.Add(new LiveValue(row.ParameterId ?? string.Empty, row.Name ?? string.Empty, row.Units ?? string.Empty, number));
            }
        }

        return values;
    }
}
