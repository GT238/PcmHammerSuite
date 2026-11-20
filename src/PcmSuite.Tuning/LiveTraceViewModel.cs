using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PcmSuite.Tune;

/// <summary>One live reading from the Logger: what it is, its units and its current number.</summary>
public sealed record LiveValue(string Id, string Name, string Units, double Value);

/// <summary>Where live readings come from. The shell implements this over the Logger; the Tune code never sees the Logger.</summary>
public interface ILiveSource
{
    /// <summary>True while the Logger is connected and logging.</summary>
    bool IsLive { get; }

    /// <summary>The latest value of every parameter the Logger is reading.</summary>
    IReadOnlyList<LiveValue> Current();
}

/// <summary>
/// Follows the engine through the open table: from the live engine speed and manifold pressure it works out which
/// cell the engine is in right now. It only reads; nothing here touches the PCM or the BIN.
/// </summary>
public sealed partial class LiveTraceViewModel : ObservableObject
{
    private readonly ILiveSource source;
    private readonly double[] rpmAxis = Array.Empty<double>();
    private readonly double[] loadAxis = Array.Empty<double>();
    private readonly bool rpmOnColumns;

    [ObservableProperty] private int _row = -1;
    [ObservableProperty] private int _column = -1;
    [ObservableProperty] private string _status = string.Empty;

    public LiveTraceViewModel(ValueEditorViewModel editor, ILiveSource source)
    {
        this.source = source;

        if (!editor.Is2D)
        {
            this.Status = "Live trace needs a table with several rows and columns.";
            return;
        }

        if (!editor.ColumnLabelsAreReal || !editor.RowLabelsAreReal)
        {
            this.Status = "This table's axis values are stored in the BIN, not listed in the XDF, so the live cell cannot be found.";
            return;
        }

        string xTitle = editor.XAxisTitle ?? string.Empty;
        string yTitle = editor.YAxisTitle ?? string.Empty;
        bool xRpm = IsRpm(xTitle);
        bool yRpm = IsRpm(yTitle);
        if (xRpm == yRpm)
        {
            this.Status = "Cannot tell which axis is engine speed (axes: '" + xTitle + "' and '" + yTitle + "').";
            return;
        }

        this.rpmOnColumns = xRpm;
        string loadTitle = this.rpmOnColumns ? yTitle : xTitle;
        if (!IsMap(loadTitle))
        {
            this.Status = "The other axis is '" + loadTitle + "'. Live trace supports tables whose load axis is manifold pressure (kPa).";
            return;
        }

        double[]? columns = Numbers(editor.Columns, editor.ColumnLabel);
        double[]? rows = Numbers(editor.Rows, editor.RowLabel);
        if (columns == null || rows == null)
        {
            this.Status = "This table's axis labels are not numbers, so the live cell cannot be found.";
            return;
        }

        this.rpmAxis = this.rpmOnColumns ? columns : rows;
        this.loadAxis = this.rpmOnColumns ? rows : columns;
        this.CanTrace = true;
        this.Status = "Waiting for the Logger. Connect it and press Start Logging.";
    }

    /// <summary>False when this table cannot be traced; <see cref="Status"/> then says why.</summary>
    public bool CanTrace { get; }

    /// <summary>Re-read the Logger's values and move the cell. Call a few times a second.</summary>
    public void Update()
    {
        if (!this.CanTrace)
        {
            return;
        }

        if (!this.source.IsLive)
        {
            this.Set(-1, -1, "Waiting for the Logger. Connect it and press Start Logging.");
            return;
        }

        IReadOnlyList<LiveValue> values = this.source.Current();
        LiveValue? rpm = values.FirstOrDefault(v =>
            (IsRpm(v.Id) || IsRpm(v.Name))
            && !v.Name.Contains("target", StringComparison.OrdinalIgnoreCase)
            && !v.Name.Contains("desired", StringComparison.OrdinalIgnoreCase)
            && !v.Id.Contains("target", StringComparison.OrdinalIgnoreCase)
            && !v.Id.Contains("desired", StringComparison.OrdinalIgnoreCase));
        LiveValue? map = values.FirstOrDefault(v => IsMapReading(v.Id) || IsMapReading(v.Name));
        if (rpm == null || map == null)
        {
            string missing = rpm == null && map == null ? "engine speed and MAP" : rpm == null ? "engine speed" : "MAP";
            this.Set(-1, -1, "The Logger is not reading " + missing + ". Enable it (the VE Tuning profile does).");
            return;
        }

        double load = map.Value;
        string loadUnits = "kPa";
        if (map.Units.Contains("psi", StringComparison.OrdinalIgnoreCase))
        {
            load *= 6.894757;
        }
        else if (map.Units.Contains("inhg", StringComparison.OrdinalIgnoreCase))
        {
            load *= 3.386389;
        }
        else if (map.Units.Contains("bar", StringComparison.OrdinalIgnoreCase))
        {
            load *= 100;
        }

        int rpmCell = VeWizardViewModel.Nearest(this.rpmAxis, rpm.Value);
        int loadCell = VeWizardViewModel.Nearest(this.loadAxis, load);
        string reading = $"{rpm.Value:0} rpm, {load:0.#} {loadUnits}";
        if (rpmCell < 0 || loadCell < 0)
        {
            this.Set(-1, -1, "Live: " + reading + " is outside this table.");
            return;
        }

        int row = this.rpmOnColumns ? loadCell : rpmCell;
        int column = this.rpmOnColumns ? rpmCell : loadCell;
        this.Set(row, column, "Live: " + reading + ".");
    }

    private void Set(int row, int column, string status)
    {
        this.Row = row;
        this.Column = column;
        this.Status = status;
    }

    private static double[]? Numbers(int count, Func<int, string> label)
    {
        double[] result = new double[count];
        for (int i = 0; i < count; i++)
        {
            if (!double.TryParse(label(i), NumberStyles.Float, CultureInfo.InvariantCulture, out result[i]))
            {
                return null;
            }
        }

        return result;
    }

    private static bool IsRpm(string text) =>
        text.Contains("rpm", StringComparison.OrdinalIgnoreCase)
        || text.Contains("engine speed", StringComparison.OrdinalIgnoreCase)
        || text.Contains("enginespeed", StringComparison.OrdinalIgnoreCase);

    private static bool IsMap(string text) =>
        text.Contains("kpa", StringComparison.OrdinalIgnoreCase)
        || text.Contains("map", StringComparison.OrdinalIgnoreCase)
        || text.Contains("manifold", StringComparison.OrdinalIgnoreCase);

    // A MAP reading, not a target or a calculated load that happens to mention it.
    private static bool IsMapReading(string text) =>
        (text.Contains("mapsensor", StringComparison.OrdinalIgnoreCase)
         || text.Contains("map sensor", StringComparison.OrdinalIgnoreCase)
         || text.Contains("manifold absolute", StringComparison.OrdinalIgnoreCase))
        && !text.Contains("target", StringComparison.OrdinalIgnoreCase)
        && !text.Contains("desired", StringComparison.OrdinalIgnoreCase);
}
