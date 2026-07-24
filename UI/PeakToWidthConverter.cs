using System.Globalization;
using System.Windows.Data;

namespace VolumeMixer.UI;

/// <summary>Maps a peak level (0..1) to a pixel width given a max width parameter.</summary>
public sealed class PeakToWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        double peak = value is float f ? f : 0;
        double max = 0;
        if (parameter is string s) double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out max);
        return Math.Clamp(peak, 0, 1) * max;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
