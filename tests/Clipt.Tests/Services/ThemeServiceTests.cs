using Clipt.Models;
using Clipt.Services;
using Microsoft.Win32;
using System.IO;

namespace Clipt.Tests.Services;

public class ThemeServiceTests : IDisposable
{
    private const string TestRegistryKeyPath = @"SOFTWARE\Clipt\Tests";
    private const string RegistryValueName = "Theme";

    public ThemeServiceTests()
    {
        CleanupTestKey();
    }

    [Fact]
    public void LoadSavedTheme_NoRegistryKey_ReturnsNormal()
    {
        var service = CreateService();

        var result = service.LoadSavedTheme();

        Assert.Equal(AppTheme.Normal, result);
    }

    [Fact]
    public void LoadSavedTheme_NormalValue_ReturnsNormal()
    {
        WriteTestRegistryValue("Normal");
        var service = CreateService();

        var result = service.LoadSavedTheme();

        Assert.Equal(AppTheme.Normal, result);
    }

    [Fact]
    public void LoadSavedTheme_DarkValue_ReturnsDark()
    {
        WriteTestRegistryValue("Dark");
        var service = CreateService();

        var result = service.LoadSavedTheme();

        Assert.Equal(AppTheme.Dark, result);
    }

    [Fact]
    public void LoadSavedTheme_InvalidValue_ReturnsNormal()
    {
        WriteTestRegistryValue("garbage_value_that_is_not_a_theme");
        var service = CreateService();

        var result = service.LoadSavedTheme();

        Assert.Equal(AppTheme.Normal, result);
    }

    [Fact]
    public void LoadSavedTheme_EmptyString_ReturnsNormal()
    {
        WriteTestRegistryValue("");
        var service = CreateService();

        var result = service.LoadSavedTheme();

        Assert.Equal(AppTheme.Normal, result);
    }

    [Fact]
    public void LoadSavedTheme_NumericString_ReturnsNormal()
    {
        WriteTestRegistryValue("99");
        var service = CreateService();

        var result = service.LoadSavedTheme();

        Assert.Equal(AppTheme.Normal, result);
    }

    [Fact]
    public void CurrentTheme_DefaultsToNormal()
    {
        var service = CreateService();

        Assert.Equal(AppTheme.Normal, service.CurrentTheme);
    }

    [Fact]
    public void ApplyTheme_PersistsToRegistry()
    {
        var service = CreateService();

        service.ApplyTheme(AppTheme.Dark);

        using var key = Registry.CurrentUser.OpenSubKey(TestRegistryKeyPath);
        var stored = key?.GetValue(RegistryValueName) as string;
        Assert.Equal("Dark", stored);
    }

    [Fact]
    public void ApplyTheme_UpdatesCurrentTheme()
    {
        var service = CreateService();

        service.ApplyTheme(AppTheme.Dark);

        Assert.Equal(AppTheme.Dark, service.CurrentTheme);
    }

    [Fact]
    public void ApplyTheme_Normal_PersistsNormal()
    {
        var service = CreateService();
        service.ApplyTheme(AppTheme.Dark);

        service.ApplyTheme(AppTheme.Normal);

        using var key = Registry.CurrentUser.OpenSubKey(TestRegistryKeyPath);
        var stored = key?.GetValue(RegistryValueName) as string;
        Assert.Equal("Normal", stored);
        Assert.Equal(AppTheme.Normal, service.CurrentTheme);
    }

    private static TestableThemeService CreateService()
    {
        return new TestableThemeService(TestRegistryKeyPath);
    }

    private static void WriteTestRegistryValue(string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(TestRegistryKeyPath);
        key.SetValue(RegistryValueName, value, RegistryValueKind.String);
    }

    private static void CleanupTestKey()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(TestRegistryKeyPath, throwOnMissingSubKey: false);
        }
        catch (ArgumentException)
        {
        }
    }

    public void Dispose()
    {
        CleanupTestKey();
        GC.SuppressFinalize(this);
    }

    private sealed class TestableThemeService : IThemeService
    {
        private readonly string _registryKeyPath;
        private const string ValueName = "Theme";

        public AppTheme CurrentTheme { get; private set; }

        public TestableThemeService(string registryKeyPath)
        {
            _registryKeyPath = registryKeyPath;
        }

        public AppTheme LoadSavedTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(_registryKeyPath);
                if (key?.GetValue(ValueName) is string value &&
                    Enum.TryParse<AppTheme>(value, ignoreCase: false, out var theme) &&
                    Enum.IsDefined(theme))
                {
                    return theme;
                }
            }
            catch (System.Security.SecurityException)
            {
            }
            catch (IOException)
            {
            }

            return AppTheme.Normal;
        }

        public void ApplyTheme(AppTheme theme)
        {
            if (!Enum.IsDefined(theme))
                theme = AppTheme.Normal;

            CurrentTheme = theme;
            PersistTheme(theme);
        }

        private void PersistTheme(AppTheme theme)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(_registryKeyPath);
                key.SetValue(ValueName, theme.ToString(), RegistryValueKind.String);
            }
            catch (System.Security.SecurityException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
