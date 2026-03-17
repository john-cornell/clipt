using System.IO;
using System.Windows;
using Clipt.Models;
using Microsoft.Win32;

namespace Clipt.Services;

public sealed class ThemeService : IThemeService
{
    private const string RegistryKeyPath = @"SOFTWARE\Clipt";
    private const string RegistryValueName = "Theme";

    public AppTheme CurrentTheme { get; private set; }

    public AppTheme LoadSavedTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            if (key?.GetValue(RegistryValueName) is string value &&
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

        var uri = theme == AppTheme.Dark
            ? new Uri("Themes/Dark.xaml", UriKind.Relative)
            : new Uri("Themes/Light.xaml", UriKind.Relative);

        var dictionary = new ResourceDictionary { Source = uri };
        var mergedDictionaries = Application.Current.Resources.MergedDictionaries;

        mergedDictionaries.Clear();
        mergedDictionaries.Add(dictionary);

        CurrentTheme = theme;
        PersistTheme(theme);
    }

    private static void PersistTheme(AppTheme theme)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
            key.SetValue(RegistryValueName, theme.ToString(), RegistryValueKind.String);
        }
        catch (System.Security.SecurityException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
