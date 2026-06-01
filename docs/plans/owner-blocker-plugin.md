# Plan: Move Clipboard Owner Blocking & Debug to Plugin Framework

**Status:** Draft  
**Target version:** 1.14.0 (host API) + `Clipt.Plugins.OwnerBlocker` 1.0.0  
**Scope:** Extract v1.13.x hardcoded blocking/debug into a first-party shipped plugin, while extending the plugin host so third-party filters and debug panels are possible.

---

WHEN IMPLEMENTING USE brutal-coder skill

AFTER IMPLEMENTATION use brutal-pr skill to review work and brutal-address-pr skill to address issues and loop until ALL issues, no matter how trivial are addressed

---

## 1. Problem Statement

Clipt v1.13.x implements clipboard polluter blocking and debug instrumentation **inside the main app**:

| Concern | Current location |
|---------|------------------|
| Block rules (process + window class) | `ClipboardBlockRules.cs`, `BlockedProcessNames.cs` |
| Registry persistence | `SettingsService` (`BlockedHistoryProcessNames`, `BlockedHistoryWindowClassPrefixes`) |
| History pipeline veto | `ClipboardHistoryService.AddAsync` |
| Debug event log + blocked lists UI | `TrayDebugTabViewModel`, Debug tab in `TrayPopupWindow.xaml` |
| Per-row Block in History | `HistoryTabViewModel.BlockOwnerAsync` |

This violates the product direction established by `Clipt.Plugins.WhereIn`: **optional tray capabilities ship as DLLs in `{exe}\Plugins\`**, not as permanent core surface area.

The plugin framework today cannot do this work:

- No clipboard-change or history-filter hooks
- No host service injection (`Activator.CreateInstance` only)
- `CliptPluginContext` is Unicode text + checkbox options only
- No dynamic tray tabs or plugin-owned settings persistence
- No way to block history without modifying host code

---

## 2. Goals

### Must have

1. **Owner blocking works identically** after migration (process name + `WisprClipboard_` class prefix, purge matching history, debug event log, per-row Block).
2. **Implemented as `Clipt.Plugins.OwnerBlocker`** — uninstallable by removing DLL; no blocking code left in core except generic pipeline.
3. **Generic host extension points** so future plugins can filter history or contribute tray UI without another host refactor.
4. **Existing registry block settings migrate** automatically on first run (HKCU `SOFTWARE\Clipt` → plugin settings file).
5. **Tests** for host pipeline + plugin rules using mocks; no regression in 551+ existing tests.

### Should have

6. History tab Block button delegates to host → plugin (not duplicated logic).
7. Debug tab becomes a **plugin-contributed tray tab** (header: "Blocker" or keep "Debug" via plugin metadata).
8. Plugin settings survive Rescan (unlike current checkbox options).

### Won't have (this phase)

- User-authored plugins in `%LOCALAPPDATA%` (still `{exe}\Plugins\` only).
- Automatic plugin updates / marketplace.
- Main-window plugin tabs.
- ALC isolation per plugin (stay on `AssemblyLoadContext.Default`).

---

## 3. Target Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│ Clipt.exe (host)                                                │
│  ClipboardListenerService → OnClipboardChangedForTray           │
│    ├─ CaptureSnapshot                                           │
│    ├─ IPluginHost.RunClipboardFilters(snapshot) → FilterVerdict  │
│    ├─ IClipboardHistoryService.AddAsync (if allowed)            │
│    └─ IPluginHost.PublishClipboardEvent(snapshot, addResult)    │
│                                                                 │
│  TrayPopupWindow                                                │
│    ├─ Clipboard / History / Groups (core tabs)                  │
│    ├─ Plugins (action plugins)                                  │
│    └─ Dynamic tabs from ICliptTrayTabPlugin[]                   │
│         └─ OwnerBlockerTabView + OwnerBlockerTabViewModel       │
└─────────────────────────────────────────────────────────────────┘
                              ▲
                              │ ICliptHost (injected at plugin init)
                              │
┌─────────────────────────────────────────────────────────────────┐
│ Plugins/Clipt.Plugins.OwnerBlocker.dll                          │
│  OwnerBlockerPlugin : ICliptClipboardFilterPlugin,              │
│                       ICliptTrayTabPlugin                       │
│  OwnerBlockRules (process + window class)                       │
│  OwnerBlockerSettingsStore (JSON per plugin id)                 │
└─────────────────────────────────────────────────────────────────┘
```

### Design principles

- **Host owns the pipeline; plugins own policy.** Host never references `Wispr`, `WisprClipboard_`, or block lists.
- **One plugin class may implement multiple capability interfaces** (same pattern as a type implementing multiple .NET interfaces).
- **Filter plugins are synchronous and fast** — called on UI thread today via `BeginInvoke`; no `await` inside filter chain initially.
- **Tray tab plugins receive `ICliptHost` once** at `Initialize(ICliptHost host)`; no static service locator.

---

## 4. New Abstractions (`Clipt.Plugins.Abstractions`)

### 4.1 Host-facing snapshot DTO

Plugins must not reference `Clipt.Models.ClipboardSnapshot` (main app assembly). Add:

```csharp
// CliptPluginClipboardSnapshot.cs
public sealed class CliptPluginClipboardSnapshot
{
    public required DateTime TimestampUtc { get; init; }
    public required uint SequenceNumber { get; init; }
    public required string OwnerProcessName { get; init; }
    public required int OwnerProcessId { get; init; }
    public nint OwnerWindowHandle { get; init; }
    public required string OwnerWindowTitle { get; init; }
    public required string OwnerWindowClass { get; init; }
    public required IReadOnlyList<CliptPluginFormatInfo> Formats { get; init; }
}
```

Host maps from `ClipboardSnapshot` in one internal adapter (`CliptPluginHostAdapters.cs`).

### 4.2 Filter plugin interface

```csharp
// ICliptClipboardFilterPlugin.cs
public interface ICliptClipboardFilterPlugin : ICliptPlugin
{
    /// <summary>Called before history add. Return Block to skip history for this snapshot.</summary>
    CliptPluginFilterVerdict Evaluate(CliptPluginClipboardSnapshot snapshot);
}

public sealed class CliptPluginFilterVerdict
{
    public bool Allow { get; init; }
    public string? Reason { get; init; }  // e.g. "Blocked process", shown in debug log
    public static CliptPluginFilterVerdict AllowSnapshot => new() { Allow = true };
    public static CliptPluginFilterVerdict BlockSnapshot(string reason) => new() { Allow = false, Reason = reason };
}
```

**Ordering:** Filters run in registration order (DLL file name sort, stable). **First block wins.** Document this; later add `[CliptPluginPriority(int)]` if needed.

### 4.3 Tray tab plugin interface

```csharp
// ICliptTrayTabPlugin.cs
public interface ICliptTrayTabPlugin : ICliptPlugin
{
    string TabHeader { get; }
    int TabOrder { get; }  // OwnerBlocker: 100 (after Plugins)
    object CreateViewModel(ICliptHost host);
}
```

Plugin ships WPF view in its own assembly:

```csharp
// ICliptTrayTabViewFactory.cs — optional, if view cannot be generic
public interface ICliptTrayTabViewFactory : ICliptTrayTabPlugin
{
    FrameworkElement CreateView(object viewModel);
}
```

Prefer **`OwnerBlockerTabView.xaml` inside plugin project** referencing same VM types; host loads tab content via `ICliptTrayTabViewFactory` to avoid host DataTemplate registry.

### 4.4 Plugin lifecycle

Extend base or add side interface:

```csharp
// ICliptPluginLifetime.cs
public interface ICliptPluginLifetime : ICliptPlugin
{
    void Initialize(ICliptHost host);
    void Shutdown();
}
```

Called from `PluginRegistry` after successful construction, before registration completes. `Shutdown` on app exit / before Rescan.

### 4.5 `ICliptHost` — controlled host API

```csharp
public interface ICliptHost
{
    // Settings (plugin-scoped JSON under %LOCALAPPDATA%\Clipt\Plugins\{pluginId}\)
    T? LoadSettings<T>() where T : class, new();
    void SaveSettings<T>(T settings) where T : class;

    // History
    Task RemoveHistoryByOwnerProcessAsync(string processName);

    // Events (for debug panels)
    event EventHandler<CliptPluginClipboardEventArgs>? ClipboardProcessed;

    // Block helper exposed for History tab UI
    Task BlockOwnerAsync(string? processName, string? windowClass);
    IReadOnlySet<string> GetBlockedProcessNames();
    IReadOnlySet<string> GetBlockedWindowClassPrefixes();
}
```

**Important:** `BlockOwnerAsync` is implemented **by the OwnerBlocker plugin** via a host callback registration, not hardcoded in host:

```csharp
// Host delegates to first plugin that registers ICliptOwnerBlockCoordinator
public interface ICliptOwnerBlockCoordinator
{
    Task BlockAsync(string? processName, string? windowClass);
    bool IsBlocked(CliptPluginClipboardSnapshot snapshot);
}
```

This keeps host neutral; History tab calls `_pluginHost.BlockOwnerAsync(...)` which dispatches to coordinator.

### 4.6 Extend `HistoryAddResult`

Replace or generalize `SkippedBlockedProcess`:

```csharp
SkippedByPluginFilter,  // Reason in debug metadata
```

Or add optional `string? FilterPluginId` + `string? FilterReason` on debug events only (enum stays coarse).

---

## 5. Host Changes (`src/Clipt`)

### 5.1 New services

| File | Responsibility |
|------|----------------|
| `Services/ICliptPluginHost.cs` | Public surface for ViewModels |
| `Services/CliptPluginHost.cs` | Filter pipeline, event publish, settings paths, coordinator dispatch |
| `Services/CliptPluginHostAdapters.cs` | Snapshot DTO mapping |
| `ViewModels/PluginTrayTabHostViewModel.cs` | Holds dynamic tab VMs; exposes `ObservableCollection<PluginTrayTabItem>` |

### 5.2 `PluginRegistry` changes

- After `Activator.CreateInstance`, if `ICliptPluginLifetime`, call `Initialize(host)`.
- Index plugins by capability:
  - `FilterPlugins: IReadOnlyList<ICliptClipboardFilterPlugin>`
  - `TrayTabPlugins: IReadOnlyList<ICliptTrayTabPlugin>`
  - `OwnerBlockCoordinator: ICliptOwnerBlockCoordinator?` (at most one; log warning if multiple)
- On `Rescan`: `Shutdown()` all lifetime plugins before clear.

### 5.3 `ClipboardHistoryService.AddAsync`

**Before** duplicate detection:

```csharp
var filterResult = _pluginHost.EvaluateFilters(snapshot);
if (!filterResult.Allow)
    return HistoryAddResult.SkippedByPluginFilter;
```

Remove direct `ClipboardBlockRules` / `SettingsService` blocked-process checks.

### 5.4 `App.xaml.cs`

- Register `ICliptPluginHost` singleton (depends on `IPluginRegistry`, `IClipboardHistoryService`, `ISettingsService` for migration only).
- Remove `TrayDebugTabViewModel` registration and `DebugTab` wiring.
- After registry init, build dynamic tray tabs from `TrayTabPlugins`.
- `OnClipboardChangedForTray`:
  ```csharp
  var addResult = hasData ? await _historyService.AddAsync(snapshot) : SkippedEmptyFormats;
  _pluginHost.PublishClipboardEvent(snapshot, addResult);
  ```
- Remove `_trayDebugTabViewModel?.RecordEvent(...)`.

### 5.5 `HistoryTabViewModel`

- Inject `ICliptPluginHost`.
- `BlockOwnerAsync` → `_pluginHost.BlockOwnerAsync(processName, windowClass: null)`.
- `IsOwnerBlocked` → `_pluginHost.IsOwnerBlocked(entry)` mapping entry owner to snapshot-like check.
- Hide Block button when no coordinator registered (plugin uninstalled).

### 5.6 `TrayPopupWindow.xaml`

- Remove hardcoded `<TabItem Header="Debug" ...>`.
- Add ItemsControl or bind `TabControl` to merged collection:
  - Core tabs (fixed order)
  - `{Binding PluginTrayTabs}` appended after Plugins tab
- Each plugin tab: `Content="{Binding View}"` or `Content="{Binding ViewModel}"` with `ContentTemplateSelector`.

### 5.7 Remove from core (delete or gut)

| Remove | Notes |
|--------|-------|
| `TrayDebugTabViewModel.cs` | Move to plugin |
| `ClipboardBlockRules.cs` | Move to plugin |
| `BlockedProcessNames.cs` | Move to plugin (or keep tiny helper in abstractions if shared) |
| `SettingsService` blocked process/class keys | Migration-only read; delete after migration |
| `ISettingsService` blocked APIs | Remove after migration |
| Debug tab XAML block (~878–1010) | Move to plugin view |

### 5.8 Settings migration

On `CliptPluginHost` first init:

1. Read legacy `HKCU\SOFTWARE\Clipt\BlockedHistoryProcessNames` and `BlockedHistoryWindowClassPrefixes`.
2. If non-empty and plugin settings empty, write to `OwnerBlocker` settings JSON.
3. Clear legacy registry keys (or leave but ignore — prefer migrate + clear to avoid split brain).

---

## 6. New Plugin Project: `Clipt.Plugins.OwnerBlocker`

### 6.1 Structure

```
src/Clipt.Plugins.OwnerBlocker/
  Clipt.Plugins.OwnerBlocker.csproj
  OwnerBlockerPlugin.cs          # ICliptClipboardFilterPlugin + ICliptTrayTabPlugin + ICliptOwnerBlockCoordinator + ICliptPluginLifetime
  OwnerBlockRules.cs             # from ClipboardBlockRules (plugin-internal)
  BlockedProcessNames.cs
  BlockedWindowClasses.cs
  OwnerBlockerSettings.cs        # { BlockedProcesses[], BlockedClassPrefixes[] }
  ViewModels/
    OwnerBlockerTabViewModel.cs  # from TrayDebugTabViewModel
  Views/
    OwnerBlockerTabView.xaml     # from Debug tab XAML
```

### 6.2 Plugin metadata

```csharp
public string Id => "clipt.plugins.owner-blocker";
public string Name => "Owner Blocker";
public string Description => "Block clipboard history from specific owner processes and window classes. Includes debug event log.";
public string TabHeader => "Blocker";
public int TabOrder => 50;
```

### 6.3 Behavior parity checklist

- [ ] Block process name (case-insensitive)
- [ ] Block `WisprClipboard_*` → prefix `WisprClipboard_`
- [ ] Block from event row, history row, "Block last owner"
- [ ] Remove matching history on block
- [ ] Two blocked lists with per-item Remove
- [ ] Clear all blocked
- [ ] Clear event log only
- [ ] Max 25 events
- [ ] Show `SkippedByPluginFilter` as "Blocked process" in UI (map reason string)
- [ ] `CanBlockOwner` / `blocked` label on rows

### 6.4 Build & ship

- Mirror `Clipt.Plugins.WhereIn.csproj` post-build copy to `Clipt\bin\{Config}\net8.0-windows\Plugins\`.
- Add to `Clipt.Tests.csproj` copy for integration tests.
- Update `installer/Clipt.iss` (already copies `Plugins\*.dll`).
- Update `build-setup.bat` verification list.

---

## 7. Implementation Phases

### Phase A — Abstractions & host API (no UI move yet)

**Version: 1.14.0**

1. Add DTOs + `ICliptClipboardFilterPlugin`, `ICliptTrayTabPlugin`, `ICliptPluginLifetime`, `ICliptHost`, `ICliptOwnerBlockCoordinator`.
2. Implement `CliptPluginHost` + registry capability indexing.
3. Wire filter pipeline into `AddAsync`.
4. Add `PublishClipboardEvent`.
5. Unit tests: filter allow/block, ordering, no coordinator graceful degradation.

**Acceptance:** Host compiles; existing tests pass; no behavioral change until plugin loaded.

### Phase B — OwnerBlocker plugin (parallel implementation)

1. Create `Clipt.Plugins.OwnerBlocker` with rules copied from core.
2. Implement all four interfaces on `OwnerBlockerPlugin`.
3. Port `TrayDebugTabViewModel` → `OwnerBlockerTabViewModel`.
4. Port Debug tab XAML → `OwnerBlockerTabView.xaml`.
5. Plugin tests mirroring `TrayDebugTabViewModelTests`, `ClipboardBlockRulesTests`.

**Acceptance:** Plugin DLL alone passes tests; manual load in host shows tab + blocking.

### Phase C — Cut over & delete core code

1. Settings migration on startup.
2. Remove Debug tab + `TrayDebugTabViewModel` from host.
3. Remove `ClipboardBlockRules` from host; remove settings keys from `ISettingsService`.
4. Dynamic plugin tabs in `TrayPopupWindow`.
5. History tab Block → `_pluginHost.BlockOwnerAsync`.
6. Bump version **1.14.0**; update `Clipt.iss`.

**Acceptance:** Full parity with v1.13.4; uninstall plugin → no blocking, no Blocker tab.

### Phase D — Polish

1. README section: authoring a filter plugin.
2. Help text for History Block when plugin missing.
3. Fix pre-existing `OnPluginOutputWritten` duplicate history race (optional but recommended while touching `App.xaml.cs`).

---

## 8. Testing Strategy

TEST ON NEW FUNCTIONALITY USING MOCKS WHERE REQUIRED

| Layer | Tests |
|-------|-------|
| `CliptPluginHost` | Filter chain, event publish, settings path, coordinator dispatch, migration |
| `PluginRegistry` | Lifetime init/shutdown, multi-interface registration, duplicate coordinator warning |
| `OwnerBlockerPlugin` | Rules, settings R/W, filter verdict, BlockAsync purges history via mock host |
| `OwnerBlockerTabViewModel` | Event log, lists, commands (port existing tests) |
| `ClipboardHistoryService` | `SkippedByPluginFilter` when mock host blocks |
| `HistoryTabViewModel` | Block hidden without coordinator; Block calls host |
| Integration | Load OwnerBlocker DLL from test output Plugins folder; end-to-end filter |

Use mock `ICliptHost` / mock coordinator — no real registry writes in unit tests.

---

## 9. Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| WPF views in plugin assembly — binding/resource issues | Plugin uses explicit `OwnerBlockerTabView` code-behind; host hosts as `ContentControl.Content = factory.CreateView(vm)` |
| Filter on UI thread — slow plugin blocks tray | Document perf contract; filter must be O(1) string checks; consider background snapshot capture later |
| User deletes plugin DLL — blocks gone | Expected; document. Optional "built-in fallback" violates goal |
| Settings split during migration | One-time migration in host; test with pre-seeded registry |
| Multiple filter plugins conflict | First-block-wins + debug reason includes plugin Id |
| `HistoryAddResult` enum change breaks serialized debug | Enum is runtime-only today — safe |

---

## 10. Version Matrix

| Component | Version |
|-----------|---------|
| Clipt host | 1.14.0 |
| Clipt.Plugins.Abstractions | 1.1.0 (new interfaces — binary break for plugin DLLs; bump and rebuild WhereIn + OwnerBlocker) |
| Clipt.Plugins.WhereIn | Rebuild only (no logic change) |
| Clipt.Plugins.OwnerBlocker | 1.0.0 (new) |
| Installer | 1.14.0 |

---

## 11. Open Questions (resolve before Phase C)

1. **Tab name:** "Blocker" vs keep "Debug"? Recommendation: **"Blocker"** — clearer for users; debug log is a sub-feature.
2. **Uninstall default:** Ship OwnerBlocker in installer (recommended) vs optional download.
3. **History row class blocking:** History entries don't store window class today — Block from history remains **process-only** unless we persist `OwnerWindowClass` on `ClipboardHistoryEntry` (optional enhancement).

---

## 12. TODO List

### Phase A — Host API
- [x] A1. Add abstractions (DTOs, filter, tray tab, lifetime, host, coordinator)
- [x] A2. Implement `CliptPluginHost` + adapter
- [x] A3. Extend `PluginRegistry` (capability index, lifetime, rescan shutdown)
- [x] A4. Integrate filter pipeline in `ClipboardHistoryService.AddAsync`
- [x] A5. Add `PublishClipboardEvent` hook in `App.xaml.cs`
- [x] A6. Tests: `CliptPluginHostTests`, filter pipeline tests

### Phase B — OwnerBlocker plugin
- [x] B1. Create `Clipt.Plugins.OwnerBlocker` project + csproj copy targets
- [x] B2. Port rules + settings store
- [x] B3. Implement `OwnerBlockerPlugin` (filter + coordinator + lifetime)
- [x] B4. Port ViewModel + View from Debug tab
- [x] B5. Tests: port `TrayDebugTabViewModelTests`, `ClipboardBlockRulesTests`

### Phase C — Cut over
- [ ] C1. Settings migration from registry → plugin JSON
- [ ] C2. Dynamic plugin tabs in `TrayPopupWindow`; remove Debug tab
- [ ] C3. Wire `HistoryTabViewModel` to `ICliptPluginHost`
- [ ] C4. Delete core blocking/debug code and settings APIs
- [ ] C5. Rebuild WhereIn; update installer/README/build-setup.bat
- [ ] C6. Version bump 1.14.0 (`Clipt.csproj`, `Clipt.iss`)

### Phase D — Polish
- [ ] D1. README plugin authoring section
- [ ] D2. Graceful UI when plugin absent
- [ ] D3. (Optional) Fix plugin output duplicate history race

### Review (mandatory)
- [ ] brutal-pr — Run brutal-pr skill to review work
- [ ] brutal-address-pr — Use brutal-address-pr skill to address all issues from review
- [ ] brutal-pr-review-loop — Finally use the brutal-pr-review-loop skill to confirm that has run properly

---

## 13. Success Criteria

1. With `Clipt.Plugins.OwnerBlocker.dll` present: Wispr blocking + debug log + blocked lists behave as v1.13.4.
2. With DLL removed: no Blocker tab, no history filtering, History Block buttons hidden/disabled.
3. `Clipt.Plugins.WhereIn` still works unchanged after abstractions bump.
4. All tests pass (551+ existing + new plugin/host tests).
5. No references to `Wispr`, `WisprClipboard_`, or `BlockedHistoryProcessNames` remain in `src/Clipt` (except migration code comments/tests).
