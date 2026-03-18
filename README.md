# Clipt

A forensic-level Windows clipboard inspector built with WPF and .NET 8.

Clipt goes far beyond simple paste — it enumerates every format present on the clipboard, shows raw bytes, text encodings, image previews, file-drop lists, HTML/RTF source, and native memory diagnostics.

## Features

- **System Tray Icon** — Always-visible notification area icon: red when clipboard is empty, green when it has data. Right-click for quick access to Open Full Window, startup mode toggle, or Exit
- **Compact Popup** — Left-click the tray icon to open a small popup showing a quick clipboard preview (text, image, or file list) without opening the full window
- **History Tab** — Placeholder tab in the compact popup, ready for clipboard history tracking in a future release
- **Expand to Full** — One-click button in the compact popup to open the full Clipt inspector window
- **Startup Mode** — Choose between launching with the full window visible or collapsed to the system tray. Preference saved to registry
- **Minimize to Tray** — Closing the main window hides it to the tray instead of exiting. Exit only via tray right-click menu
- **Text** — CF_UNICODETEXT (UTF-16), CF_TEXT (ANSI), CF_OEMTEXT with character/line counts, encoding stats, locale info
- **Hex Dump** — Configurable hex + ASCII dump for any clipboard format with selectable bytes-per-row
- **Image** — CF_BITMAP / CF_DIB / CF_DIBV5 rendered as BitmapSource with zoom, pan, and export. Shows dimensions, DPI, pixel format, stride
- **Rich Content** — CF_HTML with header offsets, CF_RTF raw source display
- **File Drop** — CF_HDROP parsed file paths with sizes and timestamps, clickable to open in Explorer
- **All Formats** — Sortable grid of every format: ID (dec/hex), name, data size, HGLOBAL handle, standard vs. registered
- **Native / Memory** — HGLOBAL addresses, GlobalLock pointers, allocation sizes, clipboard owner process info, sequence number
- **Live Monitoring** — Clipboard change listener keeps the view updated in real time
- **Light / Dark Theme** — Switchable at runtime, preference saved to registry
- **Version Display** — App version shown in title bars of both the main window and compact popup

## Downloads

| Version | Date | Installer | Notes |
|---------|------|-----------|-------|
| 1.3.0 | 2026-03-18 | [CliptSetup.exe](https://github.com/john-cornell/clipt/releases/download/v1.3.0/CliptSetup.exe) | System tray icon (red/green), compact popup with Clipboard and History tabs, expand-to-full, startup mode, minimize-to-tray, version in title bar |
| 1.2.1 | 2026-03-17 | [CliptSetup.exe](https://github.com/john-cornell/clipt/releases/download/v1.2.1/CliptSetup.exe) | Fix hex/ASCII cross-selection reliability and highlight offset |
| 1.2.0 | 2026-03-17 | [CliptSetup.exe](https://github.com/john-cornell/clipt/releases/download/v1.2.0/CliptSetup.exe) | Separate Text and Hex tabs; hex-only cross-selection between hex and ASCII columns |
| 1.1.2 | 2026-03-17 | [CliptSetup.exe](https://github.com/john-cornell/clipt/releases/download/v1.1.2/CliptSetup.exe) | Improved hex editor selection suppression logic |
| 1.1.1 | 2026-03-17 | [CliptSetup.exe](https://github.com/john-cornell/clipt/releases/download/v1.1.1/CliptSetup.exe) | Hex editor selection model, text tab improvements, expanded test coverage |
| 1.0.0 | 2026-03-17 | [CliptSetup.exe](https://github.com/john-cornell/clipt/releases/download/v1.0.0/CliptSetup.exe) | Initial release — full clipboard inspection with light/dark themes |

### System Requirements

- Windows 10 or later (x64)
- .NET 8 Desktop Runtime (bundled with installer)

### Install

1. Download `CliptSetup.exe` from the table above or from the [Releases](https://github.com/john-cornell/clipt/releases) page
2. Run the installer — it will install to Program Files
3. Launch Clipt from the Start Menu or desktop shortcut

## Known Issues

- Hex column / ASCII column cross-selection works within the Hex tab. Selection highlighting across multi-line ranges may occasionally lag on very large clipboard data.
- **History tab** is a placeholder — clipboard history tracking is not yet implemented but the tab is present in the compact notification popup, ready for a future release.
- The tray icon may initially appear in the Windows overflow area (click the `^` arrow in the system tray to find it). You can drag it to the always-visible area.

## Building from Source

```
dotnet build src\Clipt\Clipt.csproj -c Release
```

To build the installer (requires [Inno Setup](https://jrsoftware.org/isinfo.php)):

```
dotnet build src\Clipt\Clipt.csproj -c Release
iscc installer\Clipt.iss
```

## Running Tests

```
dotnet test tests\Clipt.Tests\Clipt.Tests.csproj
```

## Architecture

- **Target**: .NET 8, WPF, C# 12
- **Pattern**: MVVM with `CommunityToolkit.Mvvm`
- **P/Invoke**: Direct Win32 clipboard APIs for full format enumeration and memory inspection
- **Themes**: Runtime-switchable ResourceDictionary with registry persistence

## License

MIT
