using System.IO;
using Clipt.Models;
using Microsoft.Win32;

namespace Clipt.Services;

public sealed class SettingsService : ISettingsService
{
    private const string RegistryKeyPath = @"SOFTWARE\Clipt";
    private const string StartupModeValueName = "StartupMode";
    private const string MaxHistoryEntriesValueName = "MaxHistoryEntries";
    private const string MaxHistorySizeBytesValueName = "MaxHistorySizeBytes";
    private const int DefaultMaxHistoryEntries = 10;
    private const long DefaultMaxHistorySizeBytes = 100L * 1024 * 1024;

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

    public int LoadMaxHistoryEntries()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            if (key?.GetValue(MaxHistoryEntriesValueName) is int intVal && intVal > 0)
                return intVal;

            if (key?.GetValue(MaxHistoryEntriesValueName) is string strVal
                && int.TryParse(strVal, out int parsed) && parsed > 0)
                return parsed;
        }
        catch (System.Security.SecurityException) { }
        catch (IOException) { }

        return DefaultMaxHistoryEntries;
    }

    public void SaveMaxHistoryEntries(int count)
    {
        if (count < 1)
            count = DefaultMaxHistoryEntries;

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
            key.SetValue(MaxHistoryEntriesValueName, count, RegistryValueKind.DWord);
        }
        catch (System.Security.SecurityException) { }
        catch (UnauthorizedAccessException) { }
    }

    public long LoadMaxHistorySizeBytes()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            object? raw = key?.GetValue(MaxHistorySizeBytesValueName);

            if (raw is long longVal)
                return longVal >= 0 ? longVal : DefaultMaxHistorySizeBytes;

            if (raw is int intVal)
                return intVal >= 0 ? intVal : DefaultMaxHistorySizeBytes;

            if (raw is string strVal && long.TryParse(strVal, out long parsed))
                return parsed >= 0 ? parsed : DefaultMaxHistorySizeBytes;
        }
        catch (System.Security.SecurityException) { }
        catch (IOException) { }

        return DefaultMaxHistorySizeBytes;
    }

    public void SaveMaxHistorySizeBytes(long bytes)
    {
        if (bytes < 0)
            bytes = DefaultMaxHistorySizeBytes;

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
            key.SetValue(MaxHistorySizeBytesValueName, bytes.ToString(), RegistryValueKind.String);
        }
        catch (System.Security.SecurityException) { }
        catch (UnauthorizedAccessException) { }
    }
}
