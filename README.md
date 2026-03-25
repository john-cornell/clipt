# Clipt

A forensic-level Windows clipboard history manager and inspector built with WPF and .NET 8.

Clipt goes far beyond simple paste, it's a history manager, as well as it enumerating every format present on the clipboard, shows raw bytes, text encodings, image previews, file-drop lists, HTML/RTF source, and native memory diagnostics.

## Videos

### Featured

| Video | Watch / download |
|-------|------------------|
| **Engineering update (v1.6 → v1.10)** — features and evolution | [Release asset](https://github.com/john-cornell/clipt/releases/download/v1.10.3/Clipt_Engineering_1_6_to_1_10_update.mp4) · [In repo](media/Clipt_Engineering_1_6_to_1_10_update.mp4) |
| **Building a forensic clipboard inspector** — Win32 clipboard, `HGLOBAL`, and how Clipt inspects memory | [Release asset](https://github.com/john-cornell/clipt/releases/download/v1.10.3/Building_a_Forensic_Clipboard_Inspector__From_HGLOBAL_Pointers_.mp4) · [In repo](media/Building_a_Forensic_Clipboard_Inspector__From_HGLOBAL_Pointers_.mp4) |

Release assets match the filenames above (same links as the installer). If a link 404s, open the matching [release](https://github.com/john-cornell/clipt/releases) and download from **Assets**.

## Documents

| Document | Access |
|----------|--------|
| **Engineering update PDF (v1.6 -> v1.10)** — companion document to the update video | [Release asset](https://github.com/john-cornell/clipt/releases/download/v1.10.4/Clipt_Engineering_1_6_to_1_10_update.pdf) · [In repo](media/Clipt_Engineering_1_6_to_1_10_update.pdf) |

### Archive

| Video | Notes |
|-------|--------|
| [`Clipt_1.6_Code_Explainer.mp4`](media/Clipt_1.6_Code_Explainer.mp4) | NotebookLM-generated walkthrough of the architecture around **v1.6** — handy overview, not a low-level line-by-line; take it with a pinch of salt. Superseded for “what’s new” by the **Engineering update** video above. |

## Features

### Clipboard History

- **Clipboard History** - Tracks every clipboard entry over time with content type, summary, and timestamps. Restore or delete entries from the History tab in the compact popup
- **Clear history (keep current)** - **Clear history** in the popup History tab and **Clear history** in the tray menu remove older entries but keep any history line whose content still matches what is on the clipboard. Empty clipboard clears the full list. Purge-on-startup (optional) still wipes everything
- **Optional clear clipboard** - Tray: **Clear Clipboard When Clearing History** (like **Purge History on Startup**). Popup: **Clear clipboard too** next to **Clear history**. Both use the same saved setting; default is off. When on, the app clears the OS clipboard and then clears **all** history so the list matches (immediate refresh)
- **Named Entries** - History entries get auto-generated descriptive names (first line of text, image dimensions, file names). Single-click any name to rename it inline; text is pre-selected so you can start typing immediately. "Click to edit" tooltip guides discoverability
- **Smart Restore** - Restoring an entry writes all original clipboard formats (not just one), so paste works correctly everywhere. Restored items move to the top of the history list
- **Reorder History** - Up/down arrow buttons on each history entry let you manually reorder the list. Moving an entry to the top automatically restores it to the system clipboard
- **Delete with Auto-Advance** - Deleting the active (most recent) history entry automatically restores the next entry to the system clipboard, or clears the clipboard if history is empty
- **Image Preview** - Click inline image thumbnails in history to open a full-size DPI-aware preview popup. **Save As** button lets you export the image to PNG, BMP, JPEG, GIF, or TIFF via a format-selection file dialog
- **History Settings** - Configurable max entries (5/10/25/50), max storage size (50 MB -- unlimited), per-type filtering (text/image/files/other), and purge-on-startup option -- all from the tray right-click menu
- **Clipboard Logging** - Tray right-click **Log level** supports Off, Warn, and Debug. Logs are written to `%LOCALAPPDATA%\Clipt\clipt.log` to diagnose clipboard change timing and dedup behavior

### System Tray and Window Management

- **System Tray Icon** - Always-visible notification area icon: red when clipboard is empty, green when it has data. Right-click for quick access to Open Full Window, startup mode toggle, or Exit. The tray menu stays open while you flip settings (history limits, log level, etc.); it closes after **Open Full Window**, **Clear History**, or **Exit**
- **Compact Popup** - Left-click the tray icon to open a small popup showing a quick clipboard preview (text, image, or file list) without opening the full window
- **Pin Popup** - Pin toggle in the **top-right** of the compact popup title bar keeps the window open when you click elsewhere
- **Expand to Full** - Button **next to the title** (Clipt + version) opens the full Clipt inspector window
- **Run on Startup** - Toggle Windows startup registration from the tray menu; writes a quoted path to the current-user Run registry key (handles spaces in usernames)
- **Startup Mode** - Choose between launching with the full window visible or collapsed to the system tray. Preference saved to registry. In **collapsed** mode, the compact popup opens near the notification area on startup so it is obvious Clipt has started
- **Single instance** - Launching Clipt again (shortcut, taskbar, or `.exe`) does not start a second copy: the running instance is brought forward — **Main window** if startup mode is full, **compact popup** if collapsed
- **Minimize to Tray** - Closing the main window hides it to the tray instead of exiting. Exit only via tray right-click menu

### Clipboard Inspector

- **Text** - CF_UNICODETEXT (UTF-16), CF_TEXT (ANSI), CF_OEMTEXT with character/line counts, encoding stats, locale info
- **Hex Dump** - Configurable hex + ASCII dump for any clipboard format with selectable bytes-per-row
- **Image** - CF_BITMAP / CF_DIB / CF_DIBV5 rendered as BitmapSource with zoom, pan, and export. Shows dimensions, DPI, pixel format, stride
- **Rich Content** - CF_HTML with header offsets, CF_RTF raw source display
- **File Drop** - CF_HDROP parsed file paths with sizes and timestamps, clickable to open in Explorer
- **All Formats** - Sortable grid of every format: ID (dec/hex), name, data size, HGLOBAL handle, standard vs. registered
- **Native / Memory** - HGLOBAL addresses, GlobalLock pointers, allocation sizes, clipboard owner process info, sequence number
- **Live Monitoring** - Clipboard change listener keeps the view updated in real time
- **Light / Dark Theme** - Switchable at runtime, preference saved to registry
- **Version Display** - App version shown in title bars of both the main window and compact popup

## Downloads

Each **CliptSetup.exe** link points at a file attached to that version’s [GitHub Release](https://github.com/john-cornell/clipt/releases). If you get **404**, the release exists but no installer was uploaded yet — open the release page and check **Assets**, or upload with `gh release upload TAG installer/Output/CliptSetup.exe` after building the installer.

### Latest

| Version | Date | Installer | Notes |
|---------|------|-----------|-------|
| **1.10.5** | 2026-03-25 | [**CliptSetup.exe**](https://github.com/john-cornell/clipt/releases/download/v1.10.4/CliptSetup.exe) | Same as 1.10.3 plus engineering update **PDF** on the release (and in repo); `repomix*` gitignored — see [Videos](#videos) |

<details>
<summary><strong>Previous releases</strong> (click to expand)</summary>

| Version | Date | Installer | Notes |
|---------|------|-----------|-------|
| 1.9.8 | 2026-03-24 | [CliptSetup.exe](https://github.com/john-cornell/clipt/releases/download/v1.9.8/CliptSetup.exe) | Saved groups: durable archive aligned with history JSON (shared serialization); group save/load/restore diagnostics in `clipt.log`; safer clear-and-restore when nothing can be resolved |
| 1.9.4 | 2026-03-24 | [CliptSetup.exe](https://github.com/john-cornell/clipt/releases/download/v1.9.4/CliptSetup.exe) | Clipboard Groups: save, organize, and restore multi-entry clipboard sets from the new Groups tab and tray popup |
| 1.8.0 | 2026-03-24 | [CliptSetup.exe](https://github.com/john-cornell/clipt/releases/download/v1.8.0/CliptSetup.exe) | Manual history reordering with up/down arrows; moving an entry to the top auto-restores it to the clipboard |
| 1.7.2 | 2026-03-22 | [CliptSetup.exe](https://github.com/john-cornell/clipt/releases/download/v1.7.2/CliptSetup.exe) | Tray popup title bar: **Expand** beside app title/version; **Pin** on the far right (chrome-style hover); matches shipped binary version |
| 1.7.1 | 2026-03-22 | [Release](https://github.com/john-cornell/clipt/releases/tag/v1.7.1) (no installer uploaded) | Fix Run on Startup (quoted path for spaces); sync clipboard to history on popup open; context menu submenus open leftward; first expand/pin title bar restyle — use **1.7.2** installer above |
| 1.6.19 | 2026-03-20 | [CliptSetup.exe](https://github.com/john-cornell/clipt/releases/download/v1.6.19/CliptSetup.exe) | Image preview Save As button (PNG/BMP/JPEG/GIF/TIFF); fix preview close crash and thread-affinity crash in BitmapFrame encoding |
| 1.6.15 | 2026-03-20 | [CliptSetup.exe](https://github.com/john-cornell/clipt/releases/download/v1.6.15/CliptSetup.exe) | Tray text preview: hide 500/2K/8K/Full step controls when the clip fits the first step (no useful expansion) |
| 1.6.14 | 2026-03-20 | [CliptSetup.exe](https://github.com/john-cornell/clipt/releases/download/v1.6.14/CliptSetup.exe) | Tray context menu stays open when changing settings (toggles, history types, max entries/size, log level); still closes for Open Full Window, Clear History, and Exit |
| 1.6.13 | 2026-03-20 | [CliptSetup.exe](https://github.com/john-cornell/clipt/releases/download/v1.6.13/CliptSetup.exe) | Step-based preview expansion: Native raw hex (256→16K→Full of captured bytes), tray text (500→8K→Full), history text summaries (More/Less from blob); drop duplicate FirstBytes capture |
| 1.6.12 | 2026-03-20 | [CliptSetup.exe](https://github.com/john-cornell/clipt/releases/download/v1.6.12/CliptSetup.exe) | Fix duplicate screenshot history entries with canonical image hashing; add tray log levels (Off/Warn/Debug) and richer `clipt.log` diagnostics |
| 1.6.9 | 2026-03-19 | [CliptSetup.exe](https://github.com/john-cornell/clipt/releases/download/v1.6.9/CliptSetup.exe) | Fix IsSuppressed staying true after clear-clipboard: add .Unwrap() to Dispatcher.InvokeAsync calls so the inner async Task is fully awaited |
| 1.6.8 | 2026-03-19 | [CliptSetup.exe](https://github.com/john-cornell/clipt/releases/download/v1.6.8/CliptSetup.exe) | Clean clear-clipboard flow: single-branch instead of double-call; fix thread-safety for CaptureSnapshot in tray clear path |
| 1.6.7 | 2026-03-19 | [CliptSetup.exe](https://github.com/john-cornell/clipt/releases/download/v1.6.7/CliptSetup.exe) | **Clear clipboard too**: second history pass + immediate **Refresh** so the list goes empty with the clipboard; tray clear refreshes too |
| 1.6.6 | 2026-03-19 | [CliptSetup.exe](https://github.com/john-cornell/clipt/releases/download/v1.6.6/CliptSetup.exe) | Fix **Clear clipboard too** / tray option: run clipboard empty on the WPF UI thread (required for OpenClipboard with listener HWND) |
| 1.6.5 | 2026-03-19 | [CliptSetup.exe](https://github.com/john-cornell/clipt/releases/download/v1.6.5/CliptSetup.exe) | Tray: **Clear Clipboard When Clearing History** (same setting as popup); tray **Clear history** respects it; smoother tray clear (menu dismiss delay) |
| 1.6.4 | 2026-03-19 | [CliptSetup.exe](https://github.com/john-cornell/clipt/releases/download/v1.6.4/CliptSetup.exe) | Popup: optional **Clear clipboard too** when clearing history (default off); button renamed to **Clear history** |
| 1.6.3 | 2026-03-19 | [CliptSetup.exe](https://github.com/john-cornell/clipt/releases/download/v1.6.3/CliptSetup.exe) | Clear All / tray Clear history keeps entries matching current clipboard content; content-hash matching API |
| 1.6.2 | 2026-03-19 | [CliptSetup.exe](https://github.com/john-cornell/clipt/releases/download/v1.6.2/CliptSetup.exe) | Single-click rename with "Click to edit" tooltip and auto-select-all, fix tooltip rendering in history items, fix first-click edit flash |
| 1.6.0 | 2026-03-19 | [CliptSetup.exe](https://github.com/john-cornell/clipt/releases/download/v1.6.0/CliptSetup.exe) | Named history entries with inline rename, per-type history filtering, run-on-startup, purge-on-startup, delete-with-auto-advance, clear clipboard, configurable limits via tray menu, DI refactor with SettingsService |
| 1.4.11 | 2026-03-18 | [CliptSetup.exe](https://github.com/john-cornell/clipt/releases/download/v1.4.11/CliptSetup.exe) | Custom clipboard-eye tray icons (red/green) replacing plain circles, multi-resolution ICO with 5 sizes |
| 1.4.10 | 2026-03-18 | [CliptSetup.exe](https://github.com/john-cornell/clipt/releases/download/v1.4.10/CliptSetup.exe) | Click-to-preview full-size images, pin popup window, fix image restore (multi-format), clipboard history with dedup, inline thumbnails, code-signed |
| 1.3.0 | 2026-03-18 | [CliptSetup.exe](https://github.com/john-cornell/clipt/releases/download/v1.3.0/CliptSetup.exe) | System tray icon (red/green), compact popup with Clipboard and History tabs, expand-to-full, startup mode, minimize-to-tray, version in title bar |
| 1.2.1 | 2026-03-17 | [CliptSetup.exe](https://github.com/john-cornell/clipt/releases/download/v1.2.1/CliptSetup.exe) | Fix hex/ASCII cross-selection reliability and highlight offset |
| 1.2.0 | 2026-03-17 | [CliptSetup.exe](https://github.com/john-cornell/clipt/releases/download/v1.2.0/CliptSetup.exe) | Separate Text and Hex tabs; hex-only cross-selection between hex and ASCII columns |
| 1.1.2 | 2026-03-17 | [CliptSetup.exe](https://github.com/john-cornell/clipt/releases/download/v1.1.2/CliptSetup.exe) | Improved hex editor selection suppression logic |
| 1.1.1 | 2026-03-17 | [CliptSetup.exe](https://github.com/john-cornell/clipt/releases/download/v1.1.1/CliptSetup.exe) | Hex editor selection model, text tab improvements, expanded test coverage |
| 1.0.0 | 2026-03-17 | [CliptSetup.exe](https://github.com/john-cornell/clipt/releases/download/v1.0.0/CliptSetup.exe) | Initial release - full clipboard inspection with light/dark themes |

</details>

### System Requirements

- Windows 10 or later (x64)
- .NET 8 Desktop Runtime (bundled with installer)

### Install

1. Download `CliptSetup.exe` from **Latest** above, from **Previous releases**, or from the [Releases](https://github.com/john-cornell/clipt/releases) page
2. Run the installer - it will install to your local AppData folder (no admin required)
3. Launch Clipt from the Start Menu or desktop shortcut

### Upgrading from v1.3.0 or earlier

Versions 1.3.0 and earlier installed to **Program Files** (requiring admin). Starting with v1.4.x, Clipt installs to `%LOCALAPPDATA%\Clipt` (no admin required). The new installer **cannot** remove the old Program Files installation automatically.

**Before installing v1.4.10:**
1. Uninstall the old version via **Settings > Apps** (or Control Panel > Programs)
2. Then run the new `CliptSetup.exe`

If you skip this step, you may end up with two copies installed.

## Known Issues

- The tray icon (red = empty, green = has data) can sometimes show **empty clipboard** (red) when the clipboard actually has content, until the next clipboard change or UI refresh (e.g. opening the popup).
- Hex column / ASCII column cross-selection works within the Hex tab. Selection highlighting across multi-line ranges may occasionally lag on very large clipboard data.
- The tray icon may initially appear in the Windows overflow area (click the `^` arrow in the system tray to find it). You can drag it to the always-visible area.
- Users upgrading from v1.3.0 or earlier must manually uninstall the old version first (see Upgrading section above).

## Building from Source

```
dotnet build src\Clipt\Clipt.csproj -c Release
```

### Signed installer (`build-setup.bat`)

From the repo root, `build-setup.bat` builds **Release**, signs `Clipt.exe`, and compiles the Inno Setup installer. **Signing is always required** — the script does not skip it.

Prerequisites (if the script stops, it prints where to get each tool):

1. **SignTool** — from the [Windows SDK](https://developer.microsoft.com/windows/downloads/windows-sdk/) or Visual Studio (e.g. *Desktop development with C++* / Windows SDK). The batch file looks for `signtool` on `PATH` and under `%ProgramFiles(x86)%\Windows Kits\10\bin\<version>\x64\`.
2. **Code signing certificate** — `installer\CliptCodeSigning.pfx` (or change `SIGN_CERT` in the `.bat`).
3. **[Inno Setup 6](https://jrsoftware.org/isinfo.php)** — `ISCC.exe` in the default install path or on `PATH`.

### Installer only (unsigned / manual)

To build the installer without the batch file (requires [Inno Setup](https://jrsoftware.org/isinfo.php)):

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
