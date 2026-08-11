using System.IO;
using Clipt.Plugins;
using Microsoft.Win32;

namespace Clipt.Plugins.OwnerBlocker;

internal static class OwnerBlockerSettingsMigrator
{
    private const string RegistryKeyPath = @"SOFTWARE\Clipt";
    private const string BlockedHistoryProcessNamesValueName = "BlockedHistoryProcessNames";
    private const string BlockedHistoryWindowClassPrefixesValueName = "BlockedHistoryWindowClassPrefixes";

    public static void MigrateLegacyRegistrySettings(ICliptHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        IReadOnlySet<string> legacyProcesses = LoadLegacyProcessNames();
        IReadOnlySet<string> legacyClasses = LoadLegacyClassPrefixes();
        if (legacyProcesses.Count == 0 && legacyClasses.Count == 0)
            return;

        OwnerBlockerSettings? existing = host.LoadSettings<OwnerBlockerSettings>();
        if (existing is not null
            && (existing.BlockedProcesses.Count > 0 || existing.BlockedClassPrefixes.Count > 0))
        {
            ClearLegacyRegistryKeys();
            return;
        }

        var merged = existing ?? new OwnerBlockerSettings();
        foreach (string process in legacyProcesses)
            merged.BlockedProcesses.Add(new BlockedOwnerEntry { Name = process });
        foreach (string classPrefix in legacyClasses)
            merged.BlockedClassPrefixes.Add(new BlockedOwnerEntry { Name = classPrefix });

        merged.BlockedProcesses = merged.BlockedProcesses
            .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        merged.BlockedClassPrefixes = merged.BlockedClassPrefixes
            .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        host.SaveSettings(merged);
        ClearLegacyRegistryKeys();
    }

    private static IReadOnlySet<string> LoadLegacyProcessNames() =>
        LoadLegacySet(BlockedHistoryProcessNamesValueName);

    private static IReadOnlySet<string> LoadLegacyClassPrefixes() =>
        LoadLegacySet(BlockedHistoryWindowClassPrefixesValueName);

    private static HashSet<string> LoadLegacySet(string valueName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            if (key?.GetValue(valueName) is string raw && !string.IsNullOrWhiteSpace(raw))
            {
                var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!string.IsNullOrWhiteSpace(part))
                        result.Add(part);
                }

                return result;
            }
        }
        catch (System.Security.SecurityException) { }
        catch (IOException) { }

        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static void ClearLegacyRegistryKeys()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: true);
            if (key is null)
                return;

            key.DeleteValue(BlockedHistoryProcessNamesValueName, throwOnMissingValue: false);
            key.DeleteValue(BlockedHistoryWindowClassPrefixesValueName, throwOnMissingValue: false);
        }
        catch (System.Security.SecurityException) { }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }
}
