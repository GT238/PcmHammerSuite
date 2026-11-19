using System.Globalization;
using Avalonia.Data.Converters;

namespace PcmSuite.Tune;

/// <summary>Shows just the file name of a path.</summary>
public sealed class PathNameConverter : IValueConverter
{
    public static readonly PathNameConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string path ? Path.GetFileName(path) : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
