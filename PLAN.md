# ClipSpy — WPF MVVM Clipboard Inspector

> **WHEN IMPLEMENTING USE brutal-coder skill**
>
> **AFTER IMPLEMENTATION use brutal-pr skill to review work and brutal-address-pr skill to address issues and loop until ALL issues, no matter how trivial are addressed**

---

## Overview

ClipSpy is a C# WPF (.NET 8) MVVM application that provides **deep, forensic-level inspection** of the Windows clipboard. It goes far beyond simple "paste" — it enumerates every format present on the clipboard, shows raw bytes, text encodings, image previews, file-drop lists, HTML/RTF source, and native memory diagnostics (HGLOBAL handle addresses, allocation sizes, lock pointers). A clipboard change listener keeps the view live.

---

## Architecture

- **Target**: .NET 8, WPF, C# 12
- **Pattern**: MVVM with `CommunityToolkit.Mvvm` (ObservableObject, RelayCommand, ObservableProperty)
- **No third-party UI framework** — pure WPF with a custom dark theme (`ResourceDictionary`)
- **P/Invoke layer** for Win32 clipboard APIs (`OpenClipboard`, `CloseClipboard`, `EnumClipboardFormats`, `GetClipboardData`, `GetClipboardFormatName`, `GlobalLock`, `GlobalUnlock`, `GlobalSize`, `AddClipboardFormatListener`, `RemoveClipboardFormatListener`)

### Project Structure

```
ClipSpy/
├── ClipSpy.sln
├── src/
│   └── ClipSpy/
│       ├── ClipSpy.csproj
│       ├── App.xaml / App.xaml.cs
│       ├── Themes/
│       │   └── Dark.xaml                  — dark theme ResourceDictionary
│       ├── Native/
│       │   ├── NativeMethods.cs           — P/Invoke declarations (static partial class)
│       │   └── ClipboardConstants.cs      — CF_* constants + format-id-to-name map
│       ├── Models/
│       │   ├── ClipboardSnapshot.cs       — immutable snapshot of all formats
│       │   ├── ClipboardFormatInfo.cs     — per-format metadata (id, name, size, handle, raw bytes)
│       │   └── MemoryInfo.cs              — HGLOBAL handle, lock pointer, size, first-N-bytes preview
│       ├── Services/
│       │   ├── IClipboardService.cs       — interface
│       │   ├── ClipboardService.cs        — captures full snapshot via P/Invoke
│       │   └── ClipboardListenerService.cs— WM_CLIPBOARDUPDATE hook via HwndSource
│       ├── Converters/
│       │   └── BoolToVisibilityConverter.cs
│       ├── ViewModels/
│       │   ├── MainViewModel.cs           — orchestrates snapshot, exposes tab VMs
│       │   ├── TextTabViewModel.cs        — plain text, Unicode, ANSI, OEM, locale info
│       │   ├── HexTabViewModel.cs         — hex dump of selected format's raw bytes
│       │   ├── ImageTabViewModel.cs       — BitmapSource preview, dimensions, DPI, pixel format
│       │   ├── RichContentTabViewModel.cs — HTML source, RTF source
│       │   ├── FileDropTabViewModel.cs    — CF_HDROP file list with sizes, paths
│       │   ├── FormatsTabViewModel.cs     — master list: all format ids, names, sizes
│       │   └── NativeTabViewModel.cs      — HGLOBAL handles, pointers, GlobalSize, raw hex
│       └── Views/
│           ├── MainWindow.xaml / .cs
│           ├── TextTabView.xaml
│           ├── HexTabView.xaml
│           ├── ImageTabView.xaml
│           ├── RichContentTabView.xaml
│           ├── FileDropTabView.xaml
│           ├── FormatsTabView.xaml
│           └── NativeTabView.xaml
└── tests/
    └── ClipSpy.Tests/
        ├── ClipSpy.Tests.csproj
        ├── Services/
        │   └── ClipboardServiceTests.cs
        └── ViewModels/
            ├── MainViewModelTests.cs
            ├── TextTabViewModelTests.cs
            ├── HexTabViewModelTests.cs
            └── FormatsTabViewModelTests.cs
```

---

## Detailed Feature Specification

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

- Dark theme: `#1E1E1E` background, `#D4D4D4` foreground, accent `#007ACC`.
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
| `CommunityToolkit.Mvvm` | latest stable | ObservableObject, RelayCommand, source generators |
| `Microsoft.Extensions.DependencyInjection` | latest stable | DI container for services |
| `xunit` | latest stable | Test framework |
| `xunit.runner.visualstudio` | latest stable | VS test adapter |
| `Moq` | latest stable | Mocking for service interfaces |
| `Microsoft.NET.Test.Sdk` | latest stable | Test host |

---

## Test Plan

- **ClipboardService**: Mock P/Invoke layer behind `IClipboardService`; test snapshot construction from known byte arrays. Verify format enumeration logic, size calculations, hex formatting.
- **TextTabViewModel**: Feed known UTF-16 / ANSI byte arrays; assert character count, line count, decoded text.
- **HexTabViewModel**: Feed known bytes; assert hex dump string output matches expected format (offsets, hex pairs, ASCII sidebar).
- **FormatsTabViewModel**: Feed list of `ClipboardFormatInfo`; verify sorting, filtering, display formatting.
- **MainViewModel**: Mock `IClipboardService`; verify that `RefreshCommand` populates tab VMs correctly.

All tests use **Moq** for `IClipboardService` so no real clipboard access is needed.

---

## Implementation TODO

1. [ ] Create solution + projects (`ClipSpy.sln`, `src/ClipSpy/ClipSpy.csproj`, `tests/ClipSpy.Tests/ClipSpy.Tests.csproj`)
2. [ ] Implement `Native/NativeMethods.cs` + `ClipboardConstants.cs`
3. [ ] Implement `Models/` — `ClipboardSnapshot`, `ClipboardFormatInfo`, `MemoryInfo`
4. [ ] Implement `Services/IClipboardService.cs` + `ClipboardService.cs`
5. [ ] Implement `Services/ClipboardListenerService.cs` (WM_CLIPBOARDUPDATE via HwndSource)
6. [ ] Implement `Themes/Dark.xaml` resource dictionary
7. [ ] Implement `ViewModels/` — all tab VMs + `MainViewModel`
8. [ ] Implement `Views/` — all tab views + `MainWindow.xaml`
9. [ ] Wire DI in `App.xaml.cs`
10. [ ] Implement unit tests with Moq
11. [ ] **brutal-pr** — Run brutal-pr skill to review work
12. [ ] **brutal-address-pr** — Use brutal-address-pr skill to address all issues from review
13. [ ] **brutal-pr-review-loop** — Finally use the brutal-pr-review-loop skill to confirm that has run properly
