# Clipt — WPF MVVM Clipboard Inspector

> **WHEN IMPLEMENTING USE brutal-coder skill**
>
> **AFTER IMPLEMENTATION use brutal-pr skill to review work and brutal-address-pr skill to address issues and loop until ALL issues, no matter how trivial are addressed**

---

## Overview

Clipt is a C# WPF (.NET 8) MVVM application that provides **deep, forensic-level inspection** of the Windows clipboard. It goes far beyond simple "paste" — it enumerates every format present on the clipboard, shows raw bytes, text encodings, image previews, file-drop lists, HTML/RTF source, and native memory diagnostics (HGLOBAL handle addresses, allocation sizes, lock pointers). A clipboard change listener keeps the view live.

---

## Architecture

- **Target**: .NET 8, WPF, C# 12
- **Pattern**: MVVM with `CommunityToolkit.Mvvm` (ObservableObject, RelayCommand, ObservableProperty)
- **No third-party UI framework** — pure WPF with theme `ResourceDictionary` files
- **P/Invoke layer** for Win32 clipboard APIs (`OpenClipboard`, `CloseClipboard`, `EnumClipboardFormats`, `GetClipboardData`, `GetClipboardFormatName`, `GlobalLock`, `GlobalUnlock`, `GlobalSize`, `AddClipboardFormatListener`, `RemoveClipboardFormatListener`)

### Project Structure

```
Clipt/
├── Clipt.slnx
├── PLAN.md
├── src/
│   └── Clipt/
│       ├── Clipt.csproj
│       ├── App.xaml / App.xaml.cs
│       ├── Themes/
│       │   ├── Dark.xaml                  — dark theme ResourceDictionary
│       │   └── Light.xaml                 — light (Normal) theme ResourceDictionary   ← NEW
│       ├── Services/
│       │   ├── IThemeService.cs           — theme service interface                   ← NEW
│       │   ├── ThemeService.cs            — registry-backed theme switching            ← NEW
│       │   ├── IClipboardService.cs       — interface
│       │   ├── ClipboardService.cs        — captures full snapshot via P/Invoke
│       │   └── ClipboardListenerService.cs— WM_CLIPBOARDUPDATE hook via HwndSource
│       ├── Native/
│       │   ├── NativeMethods.cs           — P/Invoke declarations (static partial class)
│       │   └── ClipboardConstants.cs      — CF_* constants + format-id-to-name map
│       ├── Models/
│       │   ├── AppTheme.cs                — enum: Normal, Dark                        ← NEW
│       │   ├── ClipboardSnapshot.cs       — immutable snapshot of all formats
│       │   ├── ClipboardFormatInfo.cs     — per-format metadata
│       │   ├── FileDropEntry.cs
│       │   ├── FormatRow.cs
│       │   ├── FormatSelection.cs
│       │   ├── Formatting.cs
│       │   └── MemoryInfo.cs              — HGLOBAL handle, lock pointer, size
│       ├── Converters/
│       │   └── BoolToVisibilityConverter.cs
│       ├── ViewModels/
│       │   ├── MainViewModel.cs           — orchestrates snapshot, exposes tab VMs + theme toggle
│       │   ├── TextTabViewModel.cs
│       │   ├── HexTabViewModel.cs
│       │   ├── ImageTabViewModel.cs
│       │   ├── RichContentTabViewModel.cs
│       │   ├── FileDropTabViewModel.cs
│       │   ├── FormatsTabViewModel.cs
│       │   └── NativeTabViewModel.cs
│       └── Views/
│           ├── MainWindow.xaml / .cs
│           ├── TextTabView.xaml
│           ├── HexTabView.xaml
│           ├── ImageTabView.xaml
│           ├── RichContentTabView.xaml
│           ├── FileDropTabView.xaml
│           ├── FormatsTabView.xaml
│           └── NativeTabView.xaml
├── installer/
│   └── Clipt.Installer/
│       └── Clipt.Installer.wixproj       — WiX v5 MSI installer project              ← NEW
└── tests/
    └── Clipt.Tests/
        ├── Clipt.Tests.csproj
        ├── Services/
        │   ├── ClipboardServiceTests.cs
        │   └── ThemeServiceTests.cs       — theme service tests                       ← NEW
        └── ViewModels/
            ├── MainViewModelTests.cs
            ├── TextTabViewModelTests.cs
            ├── HexTabViewModelTests.cs
            └── FormatsTabViewModelTests.cs
```

---

## Feature 1: Light/Dark Theme Mode

### 1.1 `AppTheme` Enum

**File:** `src/Clipt/Models/AppTheme.cs`

```csharp
namespace Clipt.Models;

public enum AppTheme
{
    Normal = 0,
    Dark = 1
}
```

- `Normal` is the default (value `0`).
- Used throughout the app to represent the two visual modes.

### 1.2 Light Theme ResourceDictionary

**File:** `src/Clipt/Themes/Light.xaml`

A complete `ResourceDictionary` structurally identical to `Dark.xaml` — same `x:Key` names for every `Color`, `SolidColorBrush`, `FontFamily`, implicit `Style`, and named `Style`. Only the color values change.

#### Light Palette

| Key | Value | Notes |
|-----|-------|-------|
| `BackgroundColor` | `#F5F5F5` | Warm off-white, easy on the eyes |
| `SurfaceColor` | `#FFFFFF` | Pure white for panels, toolbar |
| `SurfaceLightColor` | `#E8E8E8` | Hover states, column headers |
| `BorderColor` | `#D0D0D0` | Borders, grid lines |
| `ForegroundColor` | `#1E1E1E` | Primary text — near-black |
| `ForegroundDimColor` | `#6E6E6E` | Labels, secondary text |
| `AccentColor` | `#007ACC` | Same accent as dark — brand continuity |
| `AccentLightColor` | `#1C97EA` | Hover accent — same as dark |
| `ErrorColor` | `#D32F2F` | Slightly adjusted red for light bg contrast |
| `SuccessColor` | `#2E7D32` | Slightly adjusted green for light bg contrast |

#### Structural Requirements

- Every implicit style that exists in `Dark.xaml` **must** exist in `Light.xaml` with the same target type.
- Named styles (`SectionHeader`, `MonoText`) must exist with the same `x:Key`.
- Font families (`MonoFont`, `UIFont`) are identical in both themes.
- The `Button` style's `Foreground` should be `#FFFFFF` in both themes (white text on accent background).
- The `StatusBar` and `StatusBarItem` styles use `#FFFFFF` foreground in both themes (white text on accent background).

### 1.3 `IThemeService` Interface

**File:** `src/Clipt/Services/IThemeService.cs`

```csharp
namespace Clipt.Services;

public interface IThemeService
{
    Models.AppTheme CurrentTheme { get; }
    void ApplyTheme(Models.AppTheme theme);
    Models.AppTheme LoadSavedTheme();
}
```

- `CurrentTheme` — read-only property returning the currently active theme.
- `ApplyTheme(AppTheme)` — switches the runtime `ResourceDictionary` in `Application.Current.Resources.MergedDictionaries` and persists to registry.
- `LoadSavedTheme()` — reads the saved theme from registry; returns `AppTheme.Normal` if no value found.

### 1.4 `ThemeService` Implementation

**File:** `src/Clipt/Services/ThemeService.cs`

#### Registry Storage

- **Key:** `HKEY_CURRENT_USER\SOFTWARE\Clipt`
- **Value name:** `Theme`
- **Value type:** `REG_SZ` (string)
- **Valid values:** `"Normal"`, `"Dark"`
- **Default on missing/invalid:** `AppTheme.Normal`

Use `Microsoft.Win32.Registry` / `RegistryKey` (part of the .NET BCL, no extra NuGet needed).

#### `ApplyTheme` Logic

1. Determine the dictionary URI: `theme == AppTheme.Dark` → `"Themes/Dark.xaml"`, otherwise `"Themes/Light.xaml"`.
2. Create a new `ResourceDictionary { Source = new Uri(uri, UriKind.Relative) }`.
3. Clear `Application.Current.Resources.MergedDictionaries`.
4. Add the new dictionary.
5. Write the theme name to registry (`theme.ToString()`).
6. Update `CurrentTheme`.

#### `LoadSavedTheme` Logic

1. Open `HKCU\SOFTWARE\Clipt` with `OpenSubKey` (read-only).
2. Read `"Theme"` value as string.
3. `Enum.TryParse<AppTheme>(value, out var theme)` — if success, return `theme`; otherwise return `AppTheme.Normal`.
4. If key doesn't exist, return `AppTheme.Normal`.

### 1.5 `MainViewModel` Changes

- Add constructor dependency on `IThemeService`.
- Add `[ObservableProperty] private bool _isDarkMode;` — bound to UI toggle.
- On `IsDarkMode` change (use `partial void OnIsDarkModeChanged(bool value)`): call `_themeService.ApplyTheme(value ? AppTheme.Dark : AppTheme.Normal)`.
- In `Initialize()`, load the saved theme: `var saved = _themeService.LoadSavedTheme(); IsDarkMode = saved == AppTheme.Dark; _themeService.ApplyTheme(saved);`.

### 1.6 `App.xaml` Changes

Remove the hardcoded `Dark.xaml` merge. The theme dictionary will be set at runtime by `ThemeService` via `ApplyTheme` during startup.

**Updated `App.xaml`:**

```xml
<Application x:Class="Clipt.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries />
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

### 1.7 `App.xaml.cs` Changes

- Register `IThemeService` → `ThemeService` as singleton in DI.
- After building the service provider, call `themeService.ApplyTheme(themeService.LoadSavedTheme())` **before** showing MainWindow so the correct theme is loaded on first frame.

### 1.8 `MainWindow.xaml` Changes

Add a theme toggle to the toolbar, next to the auto-refresh checkbox:

```xml
<CheckBox Content="Dark Mode" IsChecked="{Binding IsDarkMode}"
          Foreground="{DynamicResource ForegroundBrush}"
          VerticalAlignment="Center" Margin="0,0,12,0" />
```

Place it before (to the left of) the "Auto-refresh" checkbox in the right-docked `StackPanel`.

### 1.9 View `DynamicResource` Audit

All views in the project already use `DynamicResource` for brush references — this is critical because `DynamicResource` picks up runtime dictionary changes. Verify during implementation that **no** view uses `StaticResource` for any theme-defined brush or color. If found, change to `DynamicResource`.

**Exception:** Inside `Dark.xaml` and `Light.xaml` themselves, `StaticResource` is correct for referencing `Color` keys within the same dictionary. Do not change those.

---

## Feature 2: WiX v5 MSI Installer

### 2.1 Tooling

Use **WiX v5** (the latest .NET SDK-style WiX). This uses the `WixToolset.Sdk` MSBuild SDK and `.wixproj` project files — no separate WiX installer needed, it restores as a NuGet SDK.

### 2.2 Project Structure

**Directory:** `installer/Clipt.Installer/`

**Files:**
- `Clipt.Installer.wixproj` — SDK-style project referencing `WixToolset.Sdk`
- `Package.wxs` — main WiX source defining Product, Package, Directory, Components, Feature
- `Directories.wxs` — directory structure definition (optional, can be inline in Package.wxs)

### 2.3 `Clipt.Installer.wixproj`

```xml
<Project Sdk="WixToolset.Sdk/5.0.2">
  <PropertyGroup>
    <OutputType>Package</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Clipt\Clipt.csproj" />
  </ItemGroup>
</Project>
```

- The `ProjectReference` to `Clipt.csproj` causes WiX to harvest the publish output.
- No manual file listing needed — WiX v5 with `ProjectReference` auto-harvests.

### 2.4 `Package.wxs` Specification

```xml
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Package
    Name="Clipt"
    Manufacturer="Clipt"
    Version="1.0.0.0"
    UpgradeCode="{GENERATE-A-STABLE-GUID}">

    <MajorUpgrade
      DowngradeErrorMessage="A newer version of Clipt is already installed." />

    <Feature Id="Main">
      <ComponentGroupRef Id="PublishOutput" />
      <ComponentRef Id="ProgramMenuShortcut" />
    </Feature>

    <!-- Start Menu shortcut -->
    <StandardDirectory Id="ProgramMenuFolder">
      <Directory Id="CliptProgramMenu" Name="Clipt">
        <Component Id="ProgramMenuShortcut" Guid="{GENERATE-GUID}">
          <Shortcut Id="CliptShortcut"
                    Name="Clipt"
                    Target="[INSTALLFOLDER]Clipt.exe"
                    WorkingDirectory="INSTALLFOLDER" />
          <RemoveFolder Id="RemoveCliptMenu" On="uninstall" />
          <RegistryValue Root="HKCU" Key="Software\Clipt"
                         Name="Installed" Type="integer" Value="1"
                         KeyPath="yes" />
        </Component>
      </Directory>
    </StandardDirectory>
  </Package>
</Wix>
```

#### Key Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Install scope | Per-machine (`ProgramFiles`) | Standard for desktop utilities |
| Upgrade strategy | `MajorUpgrade` | Removes previous versions on upgrade |
| Shortcut | Start Menu | Standard discoverability |
| Desktop shortcut | Not included by default | Users can pin from Start Menu |
| UpgradeCode | Fixed GUID | Must never change across versions |

### 2.5 Solution File Changes

Add the installer project to `Clipt.slnx`:

```xml
<Solution>
  <Folder Name="/src/">
    <Project Path="src/Clipt/Clipt.csproj" />
  </Folder>
  <Folder Name="/installer/">
    <Project Path="installer/Clipt.Installer/Clipt.Installer.wixproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/Clipt.Tests/Clipt.Tests.csproj" />
  </Folder>
</Solution>
```

### 2.6 Build Command

```bash
dotnet build installer/Clipt.Installer/Clipt.Installer.wixproj -c Release
```

Output: `installer/Clipt.Installer/bin/Release/Clipt.Installer.msi`

---

## Detailed Feature Specification (Existing — Unchanged)

### 1. Clipboard Change Listener (`ClipboardListenerService`)

- Register via `AddClipboardFormatListener(hwnd)` on a hidden message-only window (created via `HwndSource`).
- Raises a `ClipboardChanged` event.
- Unregisters in `Dispose` via `RemoveClipboardFormatListener`.
- MainViewModel subscribes; on change → captures a new `ClipboardSnapshot` on the UI thread.

### 2. Snapshot Capture (`ClipboardService`)

- `OpenClipboard(hwnd)` → enumerate with `EnumClipboardFormats` → for each format:
  - `GetClipboardFormatName` (for registered formats) or lookup from `ClipboardConstants` (for standard CF_*).
  - `GetClipboardData(formatId)` → HGLOBAL handle.
  - `GlobalSize(handle)` → allocation size.
  - `GlobalLock(handle)` → pointer; `Marshal.Copy` first 64 KB (cap) into `byte[]`; `GlobalUnlock`.
  - Build `ClipboardFormatInfo` with id, name, size, handle address (as hex string), lock pointer (as hex string), raw bytes.
- `CloseClipboard()` in a `finally` block — **always**.
- Return `ClipboardSnapshot` with timestamp + list of `ClipboardFormatInfo`.

### 3. Tabs

| Tab | Content |
|---|---|
| **Text** | CF_UNICODETEXT decoded as UTF-16; CF_TEXT as ANSI; CF_OEMTEXT as OEM. Character count, line count, encoding stats, null-terminator position. Locale info from CF_LOCALE. |
| **Hex** | Hex + ASCII dump (16 bytes per row, offset column, printable ASCII sidebar). Dropdown to pick which format to hex-dump. Configurable bytes-per-row. |
| **Image** | CF_BITMAP / CF_DIB / CF_DIBV5 rendered as `BitmapSource`. Width, height, DPI X/Y, pixel format, stride, bits per pixel, raw palette if indexed. Zoom/scroll via `ScrollViewer`. |
| **Rich Content** | CF_HTML raw with headers (StartHTML, EndHTML offsets). CF_RTF raw source. Syntax-highlighted where feasible (at minimum monospace + coloring for tags). |
| **File Drop** | CF_HDROP parsed via `DragQueryFile`. File paths, file sizes, last-write times. Clickable to open in Explorer. |
| **All Formats** | DataGrid: Format ID (dec + hex), Format Name, Data Size (bytes, formatted), HGLOBAL Handle (hex), Is Standard (bool). Sortable columns. |
| **Native / Memory** | For the currently-selected format: HGLOBAL hex address, GlobalLock pointer hex, GlobalSize, first 256 bytes hex dump, process that owns the clipboard (via `GetClipboardOwner` + `GetWindowThreadProcessId`). Clipboard sequence number (`GetClipboardSequenceNumber`). |

### 4. UI / Theme

- **Two themes:** Normal (light) and Dark, switchable at runtime via toolbar toggle.
- Theme preference persisted in Windows registry (`HKCU\SOFTWARE\Clipt\Theme`).
- Default: **Normal** (light) mode.
- Dark theme: `#1E1E1E` background, `#D4D4D4` foreground, accent `#007ACC`.
- Light theme: `#F5F5F5` background, `#1E1E1E` foreground, accent `#007ACC`.
- `TabControl` with styled `TabItem` headers.
- Monospace font (`Cascadia Mono`, fallback `Consolas`) for hex dumps and raw content.
- Status bar: snapshot timestamp, clipboard sequence number, total formats count, clipboard owner process name + PID.

### 5. Additional P/Invoke APIs

- `GetClipboardOwner()` → HWND
- `GetWindowThreadProcessId(hwnd, out pid)` → owner PID
- `GetClipboardSequenceNumber()` → uint
- `DragQueryFile` for CF_HDROP parsing
- `IsClipboardFormatAvailable(format)` for quick checks

---

## Native Method Signatures

```csharp
[LibraryImport("user32.dll", SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
public static partial bool OpenClipboard(IntPtr hWndNewOwner);

[LibraryImport("user32.dll", SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
public static partial bool CloseClipboard();

[LibraryImport("user32.dll", SetLastError = true)]
public static partial uint EnumClipboardFormats(uint format);

[LibraryImport("user32.dll", SetLastError = true)]
public static partial IntPtr GetClipboardData(uint uFormat);

[LibraryImport("user32.dll", SetLastError = true)]
public static partial int GetClipboardFormatNameW(uint format, [Out] char[] lpszFormatName, int cchMaxCount);

[LibraryImport("user32.dll", SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
public static partial bool AddClipboardFormatListener(IntPtr hwnd);

[LibraryImport("user32.dll", SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
public static partial bool RemoveClipboardFormatListener(IntPtr hwnd);

[LibraryImport("user32.dll")]
public static partial IntPtr GetClipboardOwner();

[LibraryImport("user32.dll")]
public static partial uint GetClipboardSequenceNumber();

[LibraryImport("user32.dll")]
public static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

[LibraryImport("kernel32.dll", SetLastError = true)]
public static partial IntPtr GlobalLock(IntPtr hMem);

[LibraryImport("kernel32.dll", SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
public static partial bool GlobalUnlock(IntPtr hMem);

[LibraryImport("kernel32.dll", SetLastError = true)]
public static partial nuint GlobalSize(IntPtr hMem);

[LibraryImport("shell32.dll", SetLastError = true)]
public static partial uint DragQueryFileW(IntPtr hDrop, uint iFile, [Out] char[]? lpszFile, uint cch);
```

---

## NuGet Dependencies

| Package | Version | Purpose |
|---|---|---|
| `CommunityToolkit.Mvvm` | 8.4.0 | ObservableObject, RelayCommand, source generators |
| `Microsoft.Extensions.DependencyInjection` | 8.0.1 | DI container for services |
| `xunit` | 2.* | Test framework |
| `xunit.runner.visualstudio` | 2.* | VS test adapter |
| `Moq` | 4.* | Mocking for service interfaces |
| `Microsoft.NET.Test.Sdk` | 17.* | Test host |

---

## Test Plan

### Existing Tests (Unchanged)

- **ClipboardService**: Mock P/Invoke layer behind `IClipboardService`; test snapshot construction from known byte arrays. Verify format enumeration logic, size calculations, hex formatting.
- **TextTabViewModel**: Feed known UTF-16 / ANSI byte arrays; assert character count, line count, decoded text.
- **HexTabViewModel**: Feed known bytes; assert hex dump string output matches expected format (offsets, hex pairs, ASCII sidebar).
- **FormatsTabViewModel**: Feed list of `ClipboardFormatInfo`; verify sorting, filtering, display formatting.
- **MainViewModel**: Mock `IClipboardService`; verify that `RefreshCommand` populates tab VMs correctly.

### New Tests — ThemeService

**File:** `tests/Clipt.Tests/Services/ThemeServiceTests.cs`

Test `ThemeService` with registry access abstracted behind an interface or by using a test-specific registry subkey to avoid polluting the real registry. Alternatively, mock `IThemeService` for MainViewModel tests.

| Test | Description |
|---|---|
| `LoadSavedTheme_NoRegistryKey_ReturnsNormal` | When `HKCU\SOFTWARE\Clipt` does not exist, returns `AppTheme.Normal`. |
| `LoadSavedTheme_InvalidValue_ReturnsNormal` | When registry value is garbage string, returns `AppTheme.Normal`. |
| `LoadSavedTheme_DarkValue_ReturnsDark` | When registry value is `"Dark"`, returns `AppTheme.Dark`. |
| `LoadSavedTheme_NormalValue_ReturnsNormal` | When registry value is `"Normal"`, returns `AppTheme.Normal`. |
| `ApplyTheme_PersistsToRegistry` | After `ApplyTheme(Dark)`, reading registry returns `"Dark"`. |
| `ApplyTheme_UpdatesCurrentTheme` | After `ApplyTheme(Dark)`, `CurrentTheme` is `AppTheme.Dark`. |

### New Tests — MainViewModel Theme Integration

**File:** Update `tests/Clipt.Tests/ViewModels/MainViewModelTests.cs`

| Test | Description |
|---|---|
| `IsDarkMode_False_ByDefault` | When `IThemeService.LoadSavedTheme()` returns `Normal`, `IsDarkMode` is `false`. |
| `IsDarkMode_True_WhenSavedDark` | When `IThemeService.LoadSavedTheme()` returns `Dark`, `IsDarkMode` is `true`. |
| `ToggleIsDarkMode_CallsApplyTheme` | Setting `IsDarkMode = true` calls `ApplyTheme(AppTheme.Dark)`. |

All tests use **Moq** for `IClipboardService` and `IThemeService` so no real clipboard or registry access is needed.

---

## Implementation TODO

1. [ ] Create `Models/AppTheme.cs` enum
2. [ ] Create `Themes/Light.xaml` resource dictionary (structurally matching `Dark.xaml`)
3. [ ] Create `Services/IThemeService.cs` interface
4. [ ] Create `Services/ThemeService.cs` with registry read/write and runtime dictionary swap
5. [ ] Update `App.xaml` — remove hardcoded `Dark.xaml` merge
6. [ ] Update `App.xaml.cs` — register `IThemeService` in DI, apply saved theme on startup before showing window
7. [ ] Update `MainViewModel.cs` — add `IThemeService` dependency, `IsDarkMode` property, wire theme toggle
8. [ ] Update `MainWindow.xaml` — add Dark Mode checkbox to toolbar
9. [ ] Audit all views for `StaticResource` brush refs that should be `DynamicResource` — fix any found
10. [ ] Create `installer/Clipt.Installer/Clipt.Installer.wixproj`
11. [ ] Create `installer/Clipt.Installer/Package.wxs`
12. [ ] Update `Clipt.slnx` — add installer project
13. [ ] Write `ThemeServiceTests.cs` unit tests with mocked registry access
14. [ ] Update `MainViewModelTests.cs` with theme-related tests
15. [ ] Build and verify MSI output
16. [ ] **brutal-pr** — Run brutal-pr skill to review work
17. [ ] **brutal-address-pr** — Use brutal-address-pr skill to address all issues from review
18. [ ] **brutal-pr-review-loop** — Finally use the brutal-pr-review-loop skill to confirm that has run properly
