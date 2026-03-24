using System.Windows;
using Clipt.Converters;
using Xunit;

namespace Clipt.Tests.Converters;

public class AllTrueToVisibilityConverterTests
{
    private readonly AllTrueToVisibilityConverter _converter = new();

    [Fact]
    public void Convert_AllTrue_ReturnsVisible()
    {
        object result = _converter.Convert([true, true], typeof(Visibility), null!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(Visibility.Visible, result);
    }

    [Fact]
    public void Convert_AnyFalse_ReturnsCollapsed()
    {
        object result = _converter.Convert([true, false], typeof(Visibility), null!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(Visibility.Collapsed, result);
    }

    [Fact]
    public void Convert_UnsetValue_ReturnsCollapsed()
    {
        object result = _converter.Convert(
            [true, DependencyProperty.UnsetValue],
            typeof(Visibility),
            null!,
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(Visibility.Collapsed, result);
    }
}
