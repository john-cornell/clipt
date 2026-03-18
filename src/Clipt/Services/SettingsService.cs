using System.IO;
using Clipt.Models;
using Microsoft.Win32;

namespace Clipt.Services;

public sealed class SettingsService : ISettingsService
{
    private const string RegistryKeyPath = @"SOFTWARE\Clipt";
    private const string StartupModeValueName = "StartupMode";

    public StartupMode LoadStartupMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            if (key?.GetValue(StartupModeValueName) is string value
                && Enum.TryParse<StartupMode>(value, ignoreCase: false, out var mode)
                && Enum.IsDefined(mode))
            {
                return mode;
            }
        }
        catch (System.Security.SecurityException)
        {
        }
        catch (IOException)
        {
        }

        return StartupMode.FullWindow;
    }

    public void SaveStartupMode(StartupMode mode)
    {
        if (!Enum.IsDefined(mode))
            mode = StartupMode.FullWindow;

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
            key.SetValue(StartupModeValueName, mode.ToString(), RegistryValueKind.String);
        }
        catch (System.Security.SecurityException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
