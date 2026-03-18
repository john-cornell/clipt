using Clipt.Services;

namespace Clipt.Tests.Services;

public class TrayIconHelperTests
{
    [Fact]
    public void CreateEmptyClipboardIcon_ReturnsNonNullIcon()
    {
        using var icon = TrayIconHelper.CreateEmptyClipboardIcon();
        Assert.NotNull(icon);
    }

    [Fact]
    public void CreateHasDataIcon_ReturnsNonNullIcon()
    {
        using var icon = TrayIconHelper.CreateHasDataIcon();
        Assert.NotNull(icon);
    }

    [Fact]
    public void CreateEmptyClipboardIcon_HasExpectedSize()
    {
        using var icon = TrayIconHelper.CreateEmptyClipboardIcon();
        Assert.Equal(16, icon.Width);
        Assert.Equal(16, icon.Height);
    }

    [Fact]
    public void CreateHasDataIcon_HasExpectedSize()
    {
        using var icon = TrayIconHelper.CreateHasDataIcon();
        Assert.Equal(16, icon.Width);
        Assert.Equal(16, icon.Height);
    }

    [Fact]
    public void CreatedIcons_AreDifferent()
    {
        using var empty = TrayIconHelper.CreateEmptyClipboardIcon();
        using var hasData = TrayIconHelper.CreateHasDataIcon();
        Assert.NotSame(empty, hasData);
    }
}
