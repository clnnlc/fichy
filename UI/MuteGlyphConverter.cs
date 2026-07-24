using System.Globalization;
using System.Windows.Data;

namespace VolumeMixer.UI;

/// <summary>true → muted speaker glyph, false → speaker glyph.</summary>
public sealed class MuteGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => (value is bool b && b) ? "🔇" : "🔉";

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
