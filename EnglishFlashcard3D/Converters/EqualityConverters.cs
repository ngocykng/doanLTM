using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace EnglishFlashcard3D.Converters;

public class EqualityToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var left = value?.ToString() ?? string.Empty;
        var right = parameter?.ToString() ?? string.Empty;
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public class EqualityToBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var left = value?.ToString() ?? string.Empty;
        var right = parameter?.ToString() ?? string.Empty;
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => (value is bool b && b && parameter is not null) ? parameter.ToString()! : Binding.DoNothing;
}

public class OptionSelectedConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return false;
        var optionText = values[0]?.ToString()?.Trim() ?? string.Empty;
        var selected = values[1]?.ToString()?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(optionText) || string.IsNullOrEmpty(selected)) return false;

        var optionLetter = optionText[..1].ToUpperInvariant();
        return string.Equals(optionLetter, selected.ToUpperInvariant(), StringComparison.Ordinal);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => Array.Empty<object>();
}
