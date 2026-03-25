using Clipt.Models;
using Clipt.Services;
using Microsoft.Win32;

namespace Clipt.Tests.Services;

public class SettingsServiceTests : IDisposable
{
    private const string SettingsKeyPath = @"SOFTWARE\Clipt";
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupApprovedRunKeyPath =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string RunValueName = "Clipt";

    private readonly bool _settingsKeyExisted;
    private readonly List<RegistryValueSnapshot> _settingsValues;
    private readonly RegistryValueSnapshot? _runValue;
    private readonly RegistryValueSnapshot? _startupApprovedRunValue;

    public SettingsServiceTests()
    {
        using RegistryKey? settingsKey = Registry.CurrentUser.OpenSubKey(SettingsKeyPath);
        _settingsKeyExisted = settingsKey is not null;
        _settingsValues = settingsKey?
            .GetValueNames()
            .Select(name => new RegistryValueSnapshot(
                name,
                settingsKey.GetValue(name)!,
                settingsKey.GetValueKind(name)))
            .ToList()
            ?? [];

        using RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        _runValue = runKey?.GetValue(RunValueName) is { } runValue
            ? new RegistryValueSnapshot(
                RunValueName,
                runValue,
                runKey.GetValueKind(RunValueName))
            : null;

        using RegistryKey? approvedKey = Registry.CurrentUser.OpenSubKey(StartupApprovedRunKeyPath);
        _startupApprovedRunValue = approvedKey?.GetValue(RunValueName) is { } approvedVal
            ? new RegistryValueSnapshot(
                RunValueName,
                approvedVal,
                approvedKey.GetValueKind(RunValueName))
            : null;
    }

    public void Dispose()
    {
        RestoreSettingsKey();
        RestoreRunValue();
        RestoreStartupApprovedRunValue();
    }

    [Fact]
    public void LoadStartupMode_DefaultsToFullWindow()
    {
        var service = new SettingsService();
        var mode = service.LoadStartupMode();

        Assert.True(mode == StartupMode.FullWindow || mode == StartupMode.Collapsed);
    }

    [Fact]
    public void SaveAndLoadStartupMode_RoundTrips()
    {
        var service = new SettingsService();

        service.SaveStartupMode(StartupMode.Collapsed);
        var loaded = service.LoadStartupMode();

        Assert.Equal(StartupMode.Collapsed, loaded);

        service.SaveStartupMode(StartupMode.FullWindow);
        loaded = service.LoadStartupMode();

        Assert.Equal(StartupMode.FullWindow, loaded);
    }

    [Fact]
    public void SaveStartupMode_InvalidValue_FallsBackToFullWindow()
    {
        var service = new SettingsService();

        service.SaveStartupMode((StartupMode)999);

        var loaded = service.LoadStartupMode();
        Assert.Equal(StartupMode.FullWindow, loaded);
    }

    [Fact]
    public void ISettingsService_ImplementedBySettingsService()
    {
        ISettingsService service = new SettingsService();
        Assert.NotNull(service);
    }

    [Fact]
    public void LoadMaxHistoryEntries_DefaultsTo10()
    {
        var service = new SettingsService();
        int entries = service.LoadMaxHistoryEntries();
        Assert.True(entries > 0, "Default max history entries should be positive");
    }

    [Fact]
    public void SaveAndLoadMaxHistoryEntries_RoundTrips()
    {
        var service = new SettingsService();

        service.SaveMaxHistoryEntries(25);
        int loaded = service.LoadMaxHistoryEntries();
        Assert.Equal(25, loaded);

        service.SaveMaxHistoryEntries(10);
        loaded = service.LoadMaxHistoryEntries();
        Assert.Equal(10, loaded);
    }

    [Fact]
    public void SaveMaxHistoryEntries_InvalidValue_FallsBackToDefault()
    {
        var service = new SettingsService();
        service.SaveMaxHistoryEntries(-5);
        int loaded = service.LoadMaxHistoryEntries();
        Assert.True(loaded > 0, "Should fall back to a positive default");
    }

    [Fact]
    public void LoadMaxHistorySizeBytes_DefaultsTo100MB()
    {
        var service = new SettingsService();
        long size = service.LoadMaxHistorySizeBytes();
        Assert.True(size >= 0, "Default max history size should be non-negative");
    }

    [Fact]
    public void SaveAndLoadMaxHistorySizeBytes_RoundTrips()
    {
        var service = new SettingsService();

        service.SaveMaxHistorySizeBytes(250L * 1024 * 1024);
        long loaded = service.LoadMaxHistorySizeBytes();
        Assert.Equal(250L * 1024 * 1024, loaded);

        service.SaveMaxHistorySizeBytes(0);
        loaded = service.LoadMaxHistorySizeBytes();
        Assert.Equal(0, loaded);
    }

    [Fact]
    public void SaveMaxHistorySizeBytes_NegativeValue_FallsBackToDefault()
    {
        var service = new SettingsService();
        service.SaveMaxHistorySizeBytes(-1);
        long loaded = service.LoadMaxHistorySizeBytes();
        Assert.True(loaded >= 0, "Should fall back to a non-negative default");
    }

    [Fact]
    public void LoadPurgeHistoryOnStartup_DefaultsFalse()
    {
        var service = new SettingsService();
        service.SavePurgeHistoryOnStartup(false);
        bool loaded = service.LoadPurgeHistoryOnStartup();
        Assert.False(loaded);
    }

    [Fact]
    public void SaveAndLoadPurgeHistoryOnStartup_RoundTrips()
    {
        var service = new SettingsService();

        service.SavePurgeHistoryOnStartup(true);
        Assert.True(service.LoadPurgeHistoryOnStartup());

        service.SavePurgeHistoryOnStartup(false);
        Assert.False(service.LoadPurgeHistoryOnStartup());
    }

    [Fact]
    public void LoadClearClipboardWhenClearingHistory_DefaultsFalse()
    {
        var service = new SettingsService();
        service.SaveClearClipboardWhenClearingHistory(false);
        Assert.False(service.LoadClearClipboardWhenClearingHistory());
    }

    [Fact]
    public void SaveAndLoadClearClipboardWhenClearingHistory_RoundTrips()
    {
        var service = new SettingsService();

        service.SaveClearClipboardWhenClearingHistory(true);
        Assert.True(service.LoadClearClipboardWhenClearingHistory());

        service.SaveClearClipboardWhenClearingHistory(false);
        Assert.False(service.LoadClearClipboardWhenClearingHistory());
    }

    [Fact]
    public void LoadDisabledHistoryTypes_DefaultsEmpty()
    {
        var service = new SettingsService();
        service.SaveDisabledHistoryTypes(new HashSet<ContentType>());
        var loaded = service.LoadDisabledHistoryTypes();
        Assert.Empty(loaded);
    }

    [Fact]
    public void SaveAndLoadDisabledHistoryTypes_RoundTrips()
    {
        var service = new SettingsService();

        var disabled = new HashSet<ContentType> { ContentType.Image };
        service.SaveDisabledHistoryTypes(disabled);

        var loaded = service.LoadDisabledHistoryTypes();
        Assert.Single(loaded);
        Assert.Contains(ContentType.Image, loaded);
    }

    [Fact]
    public void SaveAndLoadDisabledHistoryTypes_MultipleTypes()
    {
        var service = new SettingsService();

        var disabled = new HashSet<ContentType> { ContentType.Text, ContentType.Files, ContentType.Other };
        service.SaveDisabledHistoryTypes(disabled);

        var loaded = service.LoadDisabledHistoryTypes();
        Assert.Equal(3, loaded.Count);
        Assert.Contains(ContentType.Text, loaded);
        Assert.Contains(ContentType.Files, loaded);
        Assert.Contains(ContentType.Other, loaded);
    }

    [Fact]
    public void SaveAndLoadDisabledHistoryTypes_IgnoresEmpty()
    {
        var service = new SettingsService();

        var disabled = new HashSet<ContentType> { ContentType.Empty, ContentType.Text };
        service.SaveDisabledHistoryTypes(disabled);

        var loaded = service.LoadDisabledHistoryTypes();
        Assert.Single(loaded);
        Assert.Contains(ContentType.Text, loaded);
        Assert.DoesNotContain(ContentType.Empty, loaded);
    }

    [Fact]
    public void LoadRunOnStartup_ReturnsBoolean()
    {
        var service = new SettingsService();
        bool loaded = service.LoadRunOnStartup();
        Assert.IsType<bool>(loaded);
    }

    [Fact]
    public void SaveRunOnStartup_True_ThenLoad_ReturnsTrue()
    {
        var service = new SettingsService();
        Assert.True(service.SaveRunOnStartup(true));
        Assert.True(service.LoadRunOnStartup());
    }

    [Fact]
    public void SaveRunOnStartup_False_ThenLoad_ReturnsFalse()
    {
        var service = new SettingsService();
        Assert.True(service.SaveRunOnStartup(false));
        Assert.False(service.LoadRunOnStartup());
    }

    [Fact]
    public void SaveRunOnStartup_True_WritesQuotedPathAndStartupApproved()
    {
        var service = new SettingsService();
        Assert.True(service.SaveRunOnStartup(true));

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        Assert.NotNull(key);

        string? raw = key.GetValue(RunValueName) as string;
        Assert.NotNull(raw);
        Assert.StartsWith("\"", raw);
        Assert.EndsWith("\"", raw);

        using var approvedKey = Registry.CurrentUser.OpenSubKey(StartupApprovedRunKeyPath);
        Assert.NotNull(approvedKey);
        Assert.Equal(RegistryValueKind.Binary, approvedKey.GetValueKind(RunValueName));
        var bin = Assert.IsType<byte[]>(approvedKey.GetValue(RunValueName));
        Assert.Equal(12, bin.Length);
        Assert.Equal(0x02, bin[0]);
    }

    [Fact]
    public void LoadRunOnStartup_False_WhenRunExistsButStartupApprovedDisabled()
    {
        var service = new SettingsService();

        using (var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            runKey.SetValue(RunValueName, "\"C:\\Fake\\Clipt.exe\"", RegistryValueKind.String);

        byte[] disabled = [0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
        using (var approvedKey = Registry.CurrentUser.CreateSubKey(StartupApprovedRunKeyPath))
            approvedKey.SetValue(RunValueName, disabled, RegistryValueKind.Binary);

        Assert.False(service.LoadRunOnStartup());
    }

    [Fact]
    public void LoadLogLevel_DefaultsToOff()
    {
        var service = new SettingsService();
        service.SaveLogLevel(AppLogLevel.Off);
        Assert.Equal(AppLogLevel.Off, service.LoadLogLevel());
    }

    [Fact]
    public void SaveAndLoadLogLevel_RoundTrips()
    {
        var service = new SettingsService();

        service.SaveLogLevel(AppLogLevel.Debug);
        Assert.Equal(AppLogLevel.Debug, service.LoadLogLevel());

        service.SaveLogLevel(AppLogLevel.Warn);
        Assert.Equal(AppLogLevel.Warn, service.LoadLogLevel());

        service.SaveLogLevel(AppLogLevel.Off);
        Assert.Equal(AppLogLevel.Off, service.LoadLogLevel());
    }

    [Fact]
    public void SaveLogLevel_InvalidValue_FallsBackToOff()
    {
        var service = new SettingsService();

        service.SaveLogLevel((AppLogLevel)999);

        Assert.Equal(AppLogLevel.Off, service.LoadLogLevel());
    }

    private void RestoreSettingsKey()
    {
        if (!_settingsKeyExisted)
        {
            Registry.CurrentUser.DeleteSubKeyTree(SettingsKeyPath, throwOnMissingSubKey: false);
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(SettingsKeyPath);
        foreach (string valueName in key.GetValueNames())
            key.DeleteValue(valueName, throwOnMissingValue: false);

        foreach (RegistryValueSnapshot snapshot in _settingsValues)
            key.SetValue(snapshot.Name, snapshot.Value, snapshot.Kind);
    }

    private void RestoreRunValue()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);

        if (_runValue is RegistryValueSnapshot snapshot)
        {
            key.SetValue(snapshot.Name, snapshot.Value, snapshot.Kind);
            return;
        }

        key.DeleteValue(RunValueName, throwOnMissingValue: false);
    }

    private void RestoreStartupApprovedRunValue()
    {
        using var key = Registry.CurrentUser.CreateSubKey(StartupApprovedRunKeyPath);

        if (_startupApprovedRunValue is RegistryValueSnapshot snapshot)
        {
            key.SetValue(snapshot.Name, snapshot.Value, snapshot.Kind);
            return;
        }

        key.DeleteValue(RunValueName, throwOnMissingValue: false);
    }

    private readonly record struct RegistryValueSnapshot(
        string Name,
        object Value,
        RegistryValueKind Kind);
}
