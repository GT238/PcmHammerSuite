using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace PcmSuite.Tune;

/// <summary>Green for the same operating system, red for different, amber when it could not be told.</summary>
public sealed class OsMatchBrushConverter : IValueConverter
{
    public static readonly OsMatchBrushConverter Instance = new();

    private static readonly IBrush Good = new SolidColorBrush(Color.Parse("#4CAF50"));
    private static readonly IBrush Bad = new SolidColorBrush(Color.Parse("#F44336"));
    private static readonly IBrush Unsure = new SolidColorBrush(Color.Parse("#E8A838"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is OsMatch match
            ? match switch { OsMatch.Same => Good, OsMatch.Different => Bad, _ => Unsure }
            : Unsure;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
