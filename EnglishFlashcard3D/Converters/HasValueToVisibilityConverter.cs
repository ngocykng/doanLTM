using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace EnglishFlashcard3D.Converters;

public class HasValueToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool visible = value switch
        {
            null => false,
            bool b => b,
            string s => !string.IsNullOrWhiteSpace(s),
            ICollection col => col.Count > 0,
            IEnumerable en => en.Cast<object>().Any(),
            int i => i != 0,
            double d => !double.IsNaN(d),
            _ => true
        };

        if (parameter is string p && p.Equals("invert", StringComparison.OrdinalIgnoreCase))
            visible = !visible;

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
