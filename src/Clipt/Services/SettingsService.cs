using System.Diagnostics;
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
    private const string PurgeHistoryOnStartupValueName = "PurgeHistoryOnStartup";
    private const string ClearClipboardWhenClearingHistoryValueName = "ClearClipboardWhenClearingHistory";
    private const string DisabledHistoryTypesValueName = "DisabledHistoryTypes";
    private const string RunRegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupApprovedRunKeyPath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string RunValueName = "Clipt";

    /// <summary>
    /// 12-byte "enabled" marker under StartupApproved\Run (same pattern Task Manager uses when enabling an item).
    /// </summary>
    private static readonly byte[] StartupApprovedEnabledBytes =
        [0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
    private const string LogLevelValueName = "LogLevel";
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

    public bool LoadPurgeHistoryOnStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            if (key?.GetValue(PurgeHistoryOnStartupValueName) is int intVal)
                return intVal != 0;
        }
        catch (System.Security.SecurityException) { }
        catch (IOException) { }

        return false;
    }

    public void SavePurgeHistoryOnStartup(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
            key.SetValue(PurgeHistoryOnStartupValueName, enabled ? 1 : 0, RegistryValueKind.DWord);
        }
        catch (System.Security.SecurityException) { }
        catch (UnauthorizedAccessException) { }
    }

    public bool LoadClearClipboardWhenClearingHistory()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            if (key?.GetValue(ClearClipboardWhenClearingHistoryValueName) is int intVal)
                return intVal != 0;
        }
        catch (System.Security.SecurityException) { }
        catch (IOException) { }

        return false;
    }

    public void SaveClearClipboardWhenClearingHistory(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
            key.SetValue(ClearClipboardWhenClearingHistoryValueName, enabled ? 1 : 0, RegistryValueKind.DWord);
        }
        catch (System.Security.SecurityException) { }
        catch (UnauthorizedAccessException) { }
    }

    public IReadOnlySet<ContentType> LoadDisabledHistoryTypes()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            if (key?.GetValue(DisabledHistoryTypesValueName) is string raw
                && !string.IsNullOrWhiteSpace(raw))
            {
                var result = new HashSet<ContentType>();
                foreach (string part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (Enum.TryParse<ContentType>(part, ignoreCase: false, out var ct)
                        && Enum.IsDefined(ct)
                        && ct != ContentType.Empty)
                    {
                        result.Add(ct);
                    }
                }

                return result;
            }
        }
        catch (System.Security.SecurityException) { }
        catch (IOException) { }

        return new HashSet<ContentType>();
    }

    public void SaveDisabledHistoryTypes(IReadOnlySet<ContentType> disabled)
    {
        ArgumentNullException.ThrowIfNull(disabled);

        string value = string.Join(",", disabled
            .Where(ct => ct != ContentType.Empty && Enum.IsDefined(ct))
            .OrderBy(ct => (int)ct)
            .Select(ct => ct.ToString()));

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
            key.SetValue(DisabledHistoryTypesValueName, value, RegistryValueKind.String);
        }
        catch (System.Security.SecurityException) { }
        catch (UnauthorizedAccessException) { }
    }

    public bool LoadRunOnStartup()
    {
        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(RunRegistryKeyPath);
            if (runKey?.GetValue(RunValueName) is not string runCommand || string.IsNullOrWhiteSpace(runCommand))
                return false;

            using var approvedKey = Registry.CurrentUser.OpenSubKey(StartupApprovedRunKeyPath);
            if (approvedKey?.GetValue(RunValueName) is not byte[] bin || bin.Length == 0)
                return true;

            return IsStartupApprovedEnabled(bin[0]);
        }
        catch (System.Security.SecurityException) { }
        catch (IOException) { }

        return false;
    }

    public bool SaveRunOnStartup(bool enabled)
    {
        try
        {
            if (!enabled)
            {
                using (var runKey = Registry.CurrentUser.CreateSubKey(RunRegistryKeyPath))
                    runKey.DeleteValue(RunValueName, throwOnMissingValue: false);

                using (var approvedKey = Registry.CurrentUser.CreateSubKey(StartupApprovedRunKeyPath))
                    approvedKey.DeleteValue(RunValueName, throwOnMissingValue: false);

                return true;
            }

            string? exePath = GetStartupExecutablePath();
            if (string.IsNullOrEmpty(exePath))
                return false;

            using (var runKey = Registry.CurrentUser.CreateSubKey(RunRegistryKeyPath))
                runKey.SetValue(RunValueName, $"\"{exePath}\"", RegistryValueKind.String);

            using (var approvedKey = Registry.CurrentUser.CreateSubKey(StartupApprovedRunKeyPath))
                approvedKey.SetValue(RunValueName, StartupApprovedEnabledBytes, RegistryValueKind.Binary);

            return true;
        }
        catch (System.Security.SecurityException) { }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }

        return false;
    }

    private static bool IsStartupApprovedEnabled(byte status) =>
        status is 0x02 or 0x06 or 0x08;

    /// <summary>
    /// Resolves the .exe to register for logon. Avoids registering dotnet.exe when developing with <c>dotnet run</c>.
    /// </summary>
    private static string? GetStartupExecutablePath()
    {
        if (IsUsableHostExecutable(Environment.ProcessPath))
            return Environment.ProcessPath;

        try
        {
            string? main = Process.GetCurrentProcess().MainModule?.FileName;
            if (IsUsableHostExecutable(main))
                return main;
        }
        catch (System.ComponentModel.Win32Exception) { }
        catch (InvalidOperationException) { }

        string[] argv = Environment.GetCommandLineArgs();
        if (argv.Length > 0 && !string.IsNullOrEmpty(argv[0]))
        {
            try
            {
                string expanded = Environment.ExpandEnvironmentVariables(argv[0]);
                string full = Path.GetFullPath(expanded);
                if (IsUsableHostExecutable(full))
                    return full;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (NotSupportedException) { }
        }

        return null;
    }

    private static bool IsUsableHostExecutable(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        try
        {
            if (!File.Exists(path))
                return false;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }

        if (string.Equals(Path.GetFileName(path), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
            return false;

        return path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
    }

    public AppLogLevel LoadLogLevel()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            if (key?.GetValue(LogLevelValueName) is string value
                && Enum.TryParse<AppLogLevel>(value, ignoreCase: true, out var level)
                && Enum.IsDefined(level))
            {
                return level;
            }
        }
        catch (System.Security.SecurityException) { }
        catch (IOException) { }

        return AppLogLevel.Off;
    }

    public void SaveLogLevel(AppLogLevel level)
    {
        if (!Enum.IsDefined(level))
            level = AppLogLevel.Off;

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
            key.SetValue(LogLevelValueName, level.ToString(), RegistryValueKind.String);
        }
        catch (System.Security.SecurityException) { }
        catch (UnauthorizedAccessException) { }
    }
}
