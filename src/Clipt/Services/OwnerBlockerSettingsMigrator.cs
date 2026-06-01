using System.IO;
using System.Text.Json;
using Clipt.Plugins;
using Microsoft.Win32;

namespace Clipt.Services;

internal static class OwnerBlockerSettingsMigrator
{
    internal const string OwnerBlockerPluginId = "clipt.plugins.owner-blocker";

    private const string RegistryKeyPath = @"SOFTWARE\Clipt";
    private const string BlockedHistoryProcessNamesValueName = "BlockedHistoryProcessNames";
    private const string BlockedHistoryWindowClassPrefixesValueName = "BlockedHistoryWindowClassPrefixes";

    private static JsonSerializerOptions JsonOptions => CliptJsonOptions.Shared;

    public static void MigrateLegacyRegistrySettings(CliptPluginHost pluginHost)
    {
        ArgumentNullException.ThrowIfNull(pluginHost);

        IReadOnlySet<string> legacyProcesses = LoadLegacyProcessNames();
        IReadOnlySet<string> legacyClasses = LoadLegacyClassPrefixes();
        if (legacyProcesses.Count == 0 && legacyClasses.Count == 0)
            return;

        OwnerBlockerMigrationSettings? existing =
            pluginHost.LoadSettings<OwnerBlockerMigrationSettings>(OwnerBlockerPluginId);

        if (existing is not null
            && (existing.BlockedProcesses.Count > 0 || existing.BlockedClassPrefixes.Count > 0))
        {
            ClearLegacyRegistryKeys();
            return;
        }

        var merged = existing ?? new OwnerBlockerMigrationSettings();
        foreach (string process in legacyProcesses)
            merged.BlockedProcesses.Add(process);
        foreach (string classPrefix in legacyClasses)
            merged.BlockedClassPrefixes.Add(classPrefix);

        merged.BlockedProcesses = merged.BlockedProcesses
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        merged.BlockedClassPrefixes = merged.BlockedClassPrefixes
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(prefix => prefix, StringComparer.OrdinalIgnoreCase)
            .ToList();

        pluginHost.SaveSettings(OwnerBlockerPluginId, merged);
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

    private sealed class OwnerBlockerMigrationSettings
    {
        public List<string> BlockedProcesses { get; set; } = [];

        public List<string> BlockedClassPrefixes { get; set; } = [];
    }
}
