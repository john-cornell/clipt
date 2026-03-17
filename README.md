# Clipt

A forensic-level Windows clipboard inspector built with WPF and .NET 8.

Clipt goes far beyond simple paste — it enumerates every format present on the clipboard, shows raw bytes, text encodings, image previews, file-drop lists, HTML/RTF source, and native memory diagnostics.

## Features

- **Text** — CF_UNICODETEXT (UTF-16), CF_TEXT (ANSI), CF_OEMTEXT with character/line counts, encoding stats, locale info
- **Hex Dump** — Configurable hex + ASCII dump for any clipboard format with selectable bytes-per-row
- **Image** — CF_BITMAP / CF_DIB / CF_DIBV5 rendered as BitmapSource with zoom, pan, and export. Shows dimensions, DPI, pixel format, stride
- **Rich Content** — CF_HTML with header offsets, CF_RTF raw source display
- **File Drop** — CF_HDROP parsed file paths with sizes and timestamps, clickable to open in Explorer
- **All Formats** — Sortable grid of every format: ID (dec/hex), name, data size, HGLOBAL handle, standard vs. registered
- **Native / Memory** — HGLOBAL addresses, GlobalLock pointers, allocation sizes, clipboard owner process info, sequence number
- **Live Monitoring** — Clipboard change listener keeps the view updated in real time
- **Light / Dark Theme** — Switchable at runtime, preference saved to registry

## Downloads

| Version | Date | Installer | Notes |
|---------|------|-----------|-------|
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

- Hex editor text selection can be a little flakey — this is known and being worked on.

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
