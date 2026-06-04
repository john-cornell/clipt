using System.Collections.ObjectModel;
using Clipt.Models;
using Clipt.Native;
using Clipt.Plugins;
using Clipt.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Clipt.ViewModels;

public sealed partial class PluginsTabViewModel : ObservableObject
{
    private readonly IPluginRegistry _registry;
    private readonly IClipboardService _clipboardService;
    private readonly Func<nint> _hwndProvider;

    private string? _clipboardText;

    [ObservableProperty]
    private string _statusText = "No plugins";

    [ObservableProperty]
    private bool _isEmpty = true;

    public ObservableCollection<PluginDisplayItem> DisplayPlugins { get; } = [];

    public ObservableCollection<PluginLoadFailureInfo> LoadFailures { get; } = [];

    public event EventHandler? PluginOutputWritten;

    public PluginsTabViewModel(
        IPluginRegistry registry,
        IClipboardService clipboardService,
        Func<nint> hwndProvider)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _clipboardService = clipboardService ?? throw new ArgumentNullException(nameof(clipboardService));
        _hwndProvider = hwndProvider ?? throw new ArgumentNullException(nameof(hwndProvider));
    }

    public void Refresh()
    {
        _registry.Initialize();
        string? saved = _clipboardText;
        RebuildDisplay();
        SetClipboardText(saved);
    }

    public void RescanPlugins()
    {
        _registry.Rescan();
        string? saved = _clipboardText;
        RebuildDisplay();
        SetClipboardText(saved);
    }

    public void SetClipboardText(string? text)
    {
        _clipboardText = text;
        foreach (PluginDisplayItem item in DisplayPlugins)
            item.NotifyClipboardChanged(BuildContext(item.OptionValues));
    }

    private void RebuildDisplay()
    {
        DisplayPlugins.Clear();
        LoadFailures.Clear();

        foreach (PluginLoadFailureInfo failure in _registry.LoadFailures)
            LoadFailures.Add(failure);

        foreach (PluginRegistrationInfo registration in _registry.Registrations)
        {
            bool isEnabled = _registry.IsPluginEnabled(registration.Plugin.Id);
            Action<bool> toggleEnabled = enabled => _registry.SetPluginEnabled(registration.Plugin.Id, enabled);

            if (registration.Plugin is ICliptTrayActionPlugin actionPlugin)
                DisplayPlugins.Add(CreateActionItem(actionPlugin, registration, isEnabled, toggleEnabled));
            else
                DisplayPlugins.Add(PluginDisplayItem.ForInfo(registration, isEnabled, toggleEnabled));
        }

        IsEmpty = DisplayPlugins.Count == 0 && LoadFailures.Count == 0;
        int actionCount = DisplayPlugins.Count(p => p.IsActionPlugin);
        int failureCount = LoadFailures.Count;
        StatusText = actionCount switch
        {
            0 when failureCount > 0 => $"{failureCount} load failure(s)",
            0 => "No plugins registered",
            1 when failureCount == 0 => "1 plugin registered",
            _ => failureCount == 0
                ? $"{actionCount} plugins registered"
                : $"{actionCount} plugin(s), {failureCount} load failure(s)",
        };
    }

    private PluginDisplayItem CreateActionItem(ICliptTrayActionPlugin plugin, PluginRegistrationInfo registration, bool isEnabled, Action<bool> toggleEnabled)
    {
        var optionValues = plugin.Options.ToDictionary(o => o.Key, o => o.DefaultValue, StringComparer.Ordinal);
        var item = new PluginDisplayItem(
            plugin,
            registration,
            optionValues,
            BuildContext(optionValues),
            RunPluginAsync,
            isEnabled,
            toggleEnabled);

        var context = BuildContext(optionValues);
        item.SetLastContext(context);
        item.NotifyClipboardChanged(context);
        return item;
    }

    private CliptPluginContext BuildContext(IReadOnlyDictionary<string, bool> optionValues) =>
        new()
        {
            ClipboardText = _clipboardText,
            OptionValues = optionValues,
        };

    private async Task RunPluginAsync(PluginDisplayItem item)
    {
        if (item.Plugin is not ICliptTrayActionPlugin actionPlugin)
            return;

        CliptPluginContext context = BuildContext(item.OptionValues);
        CliptPluginResult result = await actionPlugin.ExecuteAsync(context, CancellationToken.None)
            .ConfigureAwait(true);

        if (!result.Success || string.IsNullOrEmpty(result.OutputClipboardText))
        {
            item.LastMessage = result.Message ?? "Plugin failed.";
            item.LastMessageIsError = true;
            return;
        }

        try
        {
            _clipboardService.SetClipboardText(result.OutputClipboardText, _hwndProvider());
            item.LastMessage = result.Message ?? "Output copied to clipboard.";
            item.LastMessageIsError = false;
            PluginOutputWritten?.Invoke(this, EventArgs.Empty);
        }
        catch (InvalidOperationException ex)
        {
            item.LastMessage = ex.Message;
            item.LastMessageIsError = true;
        }
    }

    [RelayCommand]
    private void Rescan() => RescanPlugins();

    public static string? ReadUnicodeTextFromSnapshot(ClipboardSnapshot snapshot)
    {
        var unicodeFormat = snapshot.Formats
            .FirstOrDefault(f => f.FormatId == ClipboardConstants.CF_UNICODETEXT);

        if (unicodeFormat is null || unicodeFormat.RawData.Length == 0)
            return null;

        return ClipboardHistoryService.DecodeUtf16Truncated(unicodeFormat.RawData, int.MaxValue);
    }
}

public sealed partial class PluginDisplayItem : ObservableObject
{
    private readonly Func<PluginDisplayItem, Task> _runAsync;
    private readonly Action<bool>? _toggleEnabled;

    public PluginDisplayItem(
        ICliptPlugin plugin,
        PluginRegistrationInfo registration,
        Dictionary<string, bool> optionValues,
        CliptPluginContext initialContext,
        Func<PluginDisplayItem, Task> runAsync,
        bool isEnabled,
        Action<bool>? toggleEnabled)
    {
        Plugin = plugin;
        Registration = registration;
        OptionValues = optionValues;
        _runAsync = runAsync;
        _isEnabled = isEnabled;
        _toggleEnabled = toggleEnabled;
        IsActionPlugin = plugin is ICliptTrayActionPlugin;
        Options = plugin is ICliptTrayActionPlugin action
            ? action.Options.Select(o => new PluginOptionDisplayItem(o, optionValues, OnOptionChanged)).ToList()
            : [];

        if (plugin is ICliptTrayActionPlugin trayPlugin)
            CanRun = isEnabled && trayPlugin.CanExecute(initialContext);
    }

    private PluginDisplayItem(PluginRegistrationInfo registration, bool isEnabled, Action<bool>? toggleEnabled)
    {
        Plugin = registration.Plugin;
        Registration = registration;
        OptionValues = new Dictionary<string, bool>(StringComparer.Ordinal);
        _runAsync = _ => Task.CompletedTask;
        _isEnabled = isEnabled;
        _toggleEnabled = toggleEnabled;
        IsActionPlugin = false;
        Options = [];
        CanRun = false;
    }

    public ICliptPlugin Plugin { get; }

    public PluginRegistrationInfo Registration { get; }

    public bool IsActionPlugin { get; }

    public IReadOnlyList<PluginOptionDisplayItem> Options { get; }

    public Dictionary<string, bool> OptionValues { get; }

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _canRun;

    [ObservableProperty]
    private string? _lastMessage;

    [ObservableProperty]
    private bool _lastMessageIsError;

    public static PluginDisplayItem ForInfo(PluginRegistrationInfo registration, bool isEnabled, Action<bool>? toggleEnabled) =>
        new(registration, isEnabled, toggleEnabled);

    public void NotifyClipboardChanged(CliptPluginContext context)
    {
        SetLastContext(context);
        UpdateCanRun(context);
    }

    partial void OnIsEnabledChanged(bool value)
    {
        _toggleEnabled?.Invoke(value);
        if (_lastContext is not null)
            UpdateCanRun(_lastContext);
    }

    partial void OnCanRunChanged(bool value) => RunCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        LastMessage = null;
        await _runAsync(this).ConfigureAwait(true);
    }

    private CliptPluginContext? _lastContext;

    public void SetLastContext(CliptPluginContext context) => _lastContext = context;

    private void OnOptionChanged(string key, bool value)
    {
        OptionValues[key] = value;
        if (_lastContext is not null && Plugin is ICliptTrayActionPlugin actionPlugin)
        {
            CanRun = actionPlugin.CanExecute(new CliptPluginContext
            {
                ClipboardText = _lastContext.ClipboardText,
                OptionValues = OptionValues,
            });
        }
    }

    internal void UpdateCanRun(CliptPluginContext context)
    {
        if (Plugin is ICliptTrayActionPlugin actionPlugin)
            CanRun = IsEnabled && actionPlugin.CanExecute(context);
        RunCommand.NotifyCanExecuteChanged();
    }
}

public sealed partial class PluginOptionDisplayItem : ObservableObject
{
    private readonly Action<string, bool> _onChanged;

    public PluginOptionDisplayItem(
        CliptPluginOption option,
        Dictionary<string, bool> values,
        Action<string, bool> onChanged)
    {
        Key = option.Key;
        Label = option.Label;
        _onChanged = onChanged;
        _isChecked = values.TryGetValue(option.Key, out bool value) ? value : option.DefaultValue;
        values[Key] = _isChecked;
    }

    public string Key { get; }

    public string Label { get; }

    [ObservableProperty]
    private bool _isChecked;

    partial void OnIsCheckedChanged(bool value) => _onChanged(Key, value);
}
