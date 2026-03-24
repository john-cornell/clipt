using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Clipt.Converters;

/// <summary>
/// Visible when every bound value is <c>true</c>; otherwise collapsed.
/// </summary>
public sealed class AllTrueToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values is null || values.Length == 0)
            return Visibility.Collapsed;

        foreach (object? v in values)
        {
            if (ReferenceEquals(v, DependencyProperty.UnsetValue))
                return Visibility.Collapsed;
            if (v is not bool b || !b)
                return Visibility.Collapsed;
        }

        return Visibility.Visible;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
