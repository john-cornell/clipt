# Plan: Groups Tab — Name Column Resizer & Per-Item Management

**Status:** Draft
**Target version:** next patch after 1.14.15
**Scope:** Two independent Groups-tab UX gaps: (1) long group names get clipped by the fixed-width count/time/menu/buttons area with no way to widen the name column, and (2) a saved group has no way to inspect, rename, delete, or reorder the individual clips inside it — only whole-group operations exist today.

---

WHEN IMPLEMENTING USE brutal-coder skill

AFTER IMPLEMENTATION use brutal-pr skill to review work and brutal-address-pr skill to address issues and loop until ALL issues, no matter how trivial are addressed

---

## 1. Problem Statement

Each group row in `TrayPopupWindow.xaml` (Groups tab) is rendered by `ItemsControl.ItemTemplate` as an independent `Grid` per row — there is no shared column definition across rows. The name column is `Width="*"` with `TextTrimming="CharacterEllipsis"`; a long group name is silently truncated with no way to see the rest short of renaming it, and no splitter exists because a splitter would only affect the one row it lives on.

Separately, `ClipboardGroup` (the in-memory model) only tracks `EntryIds` — a bare list of archived-entry IDs used to resolve blob files and drive restore order. The rich per-clip metadata (`Name`, `Summary`, `ContentType`, `DataSizeBytes`, `TimestampUtc`) exists only in `groups.json`'s `ArchivedEntries` and is read from disk only transiently, inside `ClipboardGroupService.WriteGroupsFileAsync`, to round-trip it back out unchanged on every write. `LoadAsync` extracts `Id`s from `ArchivedEntries` and discards the rest. There is currently no way to see what's inside a saved group without restoring it, and no way to fix a mis-named clip, drop one bad item, or reorder the clips within a group — the whole group must be deleted and re-saved.

## 2. Goals

### Must have

1. A draggable splitter between the group name and the count/time/menu/buttons area. Dragging any row's splitter resizes the name column for **all** group rows (shared width), and the width persists across restarts.
2. Folder header rows (`GroupSectionDisplayItem`) are **not** affected — their name column stays as-is.
3. Each group row gets an expand/collapse chevron. Expanding reveals the group's saved clips, one row each, showing name, content-type/size summary, and relative time.
4. Per-item **rename** (click-to-edit, same interaction as group/folder rename today), **delete** (with the underlying blob file removed), and **reorder** (up/down arrows, always available — no sort-mode gating, since there is no other ordering concept for clips within a group).
5. Reordering/deleting items changes `EntryIds` order/content, which changes **restore** order/content — this is expected and matches "the items in the group," not a separate display-only order.
6. Deleting the last item in a group deletes the group itself (an empty saved group has nothing to restore).
7. None of this touches the live clipboard or `IClipboardHistoryService` — it is entirely `ClipboardGroupService`-internal (in-memory `_groups`, `groups.json`, and files under `groups/{groupId}/blobs/`). Restoring to the clipboard remains the existing, untouched `RestoreGroupForIdAsync` path.
8. Tests for every new service method and the view-model changes; no regression in the existing suite.

### Won't have (this phase)

- Drag-and-drop reordering of items within a group (up/down arrows only, consistent with existing group/folder reorder controls).
- Resizing any column other than group-row name vs. the rest.
- Editing a clip's actual content (text/image/etc.) from the expand panel — name (metadata) only.

---

## 3. Data Model

### 3.1 New public DTO: `ArchivedGroupEntryInfo`

```csharp
// src/Clipt/Models/ArchivedGroupEntryInfo.cs
namespace Clipt.Models;

public sealed record ArchivedGroupEntryInfo(
    string Id,
    string Name,
    string Summary,
    ContentType ContentType,
    long DataSizeBytes,
    DateTime TimestampUtc);
```

A public counterpart to the private `ArchivedGroupEntryDto` already in `ClipboardGroupService` — that DTO stays as the serialization type; this record is the read model exposed to the rest of the app so the view layer never needs `System.Text.Json` types.

### 3.2 `ClipboardGroup` additions

```csharp
public sealed class ClipboardGroup
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public required DateTime CreatedUtc { get; init; }
    public required IReadOnlyList<string> EntryIds { get; init; }
    public string? FolderId { get; set; }

    /// <summary>Rich per-clip metadata, same order as EntryIds. Empty if ArchivedEntries couldn't be resolved (e.g. hand-edited groups.json).</summary>
    public required IReadOnlyList<ArchivedGroupEntryInfo> Entries { get; init; }
}
```

`Entries` is populated once, at `LoadAsync`/`SaveGroupAsync`/`ImportGroupFromPackageAsync` time, directly from the `ArchivedGroupEntryDto` list already being built/read at each of those call sites (`BuildArchivedEntriesAsync`, `dto.ArchivedEntries`) — no new disk reads. `EntryIds` stays as the derived `Entries.Select(e => e.Id)` list at every construction site (kept as an explicit property, not computed, since `RestoreGroupForIdAsync` and `ItemCountText` read it directly and changing that ripples further than this plan's scope).

Every existing `ClipboardGroup` object initializer (`LoadAsync`, `SaveGroupAsync`, `ImportGroupFromPackageAsync`, and the three new mutators below) is updated to set `Entries`.

### 3.3 Persistence (`groups.json`)

No format change — `ArchivedGroupEntryDto` already carries every field `ArchivedGroupEntryInfo` needs. Only the in-memory load path changes (stop discarding it).

---

## 4. Service Changes (`IClipboardGroupService` / `ClipboardGroupService`)

```csharp
public interface IClipboardGroupService
{
    // ... existing members unchanged ...

    /// <summary>Renames one clip within a group (metadata only — does not touch its blob).</summary>
    Task RenameGroupEntryAsync(string groupId, string entryId, string newName);

    /// <summary>Removes one clip from a group and deletes its blob file. If this empties the group, deletes the whole group (and its archive folder) instead of leaving a 0-item group.</summary>
    Task DeleteGroupEntryAsync(string groupId, string entryId);

    /// <summary>Reorders a clip within its group. direction: -1 toward index 0, +1 toward the end. No-op at a boundary or unknown id. Changes restore order.</summary>
    Task MoveGroupEntryAsync(string groupId, string entryId, int direction);
}
```

All three follow the exact pattern already used by `RenameGroupAsync`/`DeleteGroupAsync`/`MoveGroupAsync`: `_gate.WaitAsync()`, mutate `_groups` (replace the `ClipboardGroup` at its index, since `Entries`/`EntryIds` are `init`-only — same "rebuild the record" style already used for other mutations of `required`/`init` properties on this model), call `WriteGroupsFileAsync(archivedByGroupId: ...)` passing the updated `ArchivedGroupEntryDto` list for that one group so the merge-write in `WriteGroupsFileAsync` persists it instead of pulling the stale on-disk copy, then raise `GroupsChanged`.

`DeleteGroupEntryAsync` implementation notes:
- Delete `groups/{groupId}/blobs/{entryId}.bin` via the same "quiet" delete helper pattern as `DeleteGroupArchiveQuietly` (log-and-continue on `IOException`, never throw from a UI-triggered delete).
- If the resulting `Entries`/`EntryIds` count is 0, skip the `archivedByGroupId` write entirely and instead call the existing `DeleteGroupAsync(groupId)` logic (remove from `_groups`, `DeleteGroupArchiveQuietly`, write) so the empty-group case reuses one code path rather than duplicating cleanup.

`MoveGroupEntryAsync` swaps adjacent entries in both `Entries` and the parallel `EntryIds` list (kept in lockstep) — same adjacent-swap approach as `ClipboardHistoryService.MoveAsync`/`ClipboardGroupService.MoveGroupAsync`, not a full re-sort.

---

## 5. View Model Changes (`GroupsTabViewModel`)

### 5.1 Shared, persisted name-column width

```csharp
[ObservableProperty]
private double _nameColumnWidth;
```

- Constructor loads it via a new `ISettingsService.LoadGroupNameColumnWidth()` (registry-backed, `REG_SZ` or `REG_DWORD` storing a rounded double, default `120`, same pattern as every other `SettingsService` member — see `LoadGroupSortMode`/`SaveGroupSortMode` for the exact shape to mirror).
- `partial void OnNameColumnWidthChanged(double value)` clamps to a sane minimum (e.g. 60) and calls `SaveGroupNameColumnWidth(value)`.
- This lives on `GroupsTabViewModel` (one instance, shared `DataContext` for the whole Groups tab), not on `GroupDisplayItem` — every row's Grid binds to the same property instance, so one drag updates every row.

### 5.2 Per-item display

```csharp
public sealed partial class GroupDisplayItem : ObservableObject
{
    // ... existing members unchanged ...

    [ObservableProperty]
    private bool _isExpanded;

    public required IRelayCommand ToggleExpandCommand { get; init; }

    public ObservableCollection<GroupEntryDisplayItem> Entries { get; } = [];
}

public sealed partial class GroupEntryDisplayItem : ObservableObject
{
    public required string Id { get; init; }

    [ObservableProperty]
    private string _name = string.Empty;

    public required string DetailText { get; init; }   // e.g. "Text · 1.2 KB"
    public required string RelativeTime { get; init; }

    [ObservableProperty]
    private bool _isEditing;

    public required IAsyncRelayCommand<string> RenameCommand { get; init; }
    public required IAsyncRelayCommand DeleteCommand { get; init; }

    /// <summary>Non-null except at the boundary of the group's item list.</summary>
    public IAsyncRelayCommand? MoveUpCommand { get; init; }
    public IAsyncRelayCommand? MoveDownCommand { get; init; }
}
```

`BuildSection` populates `GroupDisplayItem.Entries` straight from `group.Entries` (already in memory — no I/O), wiring `RenameCommand`/`DeleteCommand`/`MoveUpCommand`/`MoveDownCommand` to the three new service methods exactly as the existing group-level commands wire to `RenameGroupAsync`/`DeleteGroupAsync`/`MoveGroupAsync`. `DetailText` is built once, e.g. `$"{entry.ContentType} · {FormatSize(entry.DataSizeBytes)}"`, reusing whatever size-formatting helper already exists for history entries (check `HistoryTabViewModel` before adding a new one).

`ToggleExpandCommand` flips `IsExpanded` — pure UI state, not persisted (mirrors that folder/section collapse *is* persisted but per-group expand state, being transient inspection rather than an organizing decision, isn't; consistent with there being no persisted "is this group's detail panel open" concept anywhere else in the app).

`OnGroupDisplayPropertyChanged` (already subscribed for `IsEditing` → `AnyGroupEditing`) also needs `GroupEntryDisplayItem.IsEditing` wired into `AnyGroupEditing` (the tray popup uses this to avoid closing mid-rename) — subscribe to each `GroupEntryDisplayItem.PropertyChanged` the same way group items are subscribed today, and unsubscribe in `Refresh()`'s teardown loop alongside the existing `oldGroup.PropertyChanged -= ...`.

---

## 6. UI Changes (`TrayPopupWindow.xaml` / `.xaml.cs`)

### 6.1 Resizer

- New converter `DoubleToGridLengthConverter` (registered in `Window.Resources` next to `BoolToVis`).
- Group row `Grid.ColumnDefinitions` gains one more `Auto` column for a `GridSplitter`, plus a `*` filler column after it so the count/time/menu/buttons stay right-aligned exactly as today:

  `expand-chevron (Auto) | move-up/down (Auto) | name (bound width, MinWidth=60) | splitter (Auto, 6px) | filler (*) | item-count (Auto) | relative-time (Auto) | menu (Auto) | move-to-folder (Auto) | delete (Auto)`

- The name column binds `Width` two-way through the converter to `NameColumnWidth` on `GroupsTabViewModel`. Since the row's `DataContext` is the `GroupDisplayItem`, not the tab view model, the binding needs an explicit path to the tab's `DataContext`: give the `TabItem` (`<TabItem Header="Groups" DataContext="{Binding GroupsTab}">`) an `x:Name`, and bind with `{Binding ElementName=GroupsTabItem, Path=DataContext.NameColumnWidth, Converter=...}`.
- `GridSplitter` uses `ShowsPreview="True"` so the bound value (and the registry write it triggers) only updates on drag release, not on every mouse-move frame.

### 6.2 Expand panel

- Group row `DataTemplate` changes from a bare `Grid` to a `StackPanel` containing the existing `Border`/`Grid` row plus a nested `ItemsControl ItemsSource="{Binding Entries}" Visibility="{Binding IsExpanded, Converter={StaticResource BoolToVis}}"` — the same structural pattern already used one level up for folder-section → group-row nesting.
- New leftmost column on the group row: an expand/collapse chevron button (▼/▶), same `Style.Triggers`-on-`IsExpanded` pattern as the folder header's existing `ToggleCollapsedCommand` button.
- Each nested item row: name (click-to-edit `TextBlock`/`TextBox` pair, identical interaction to `GroupEntryName_MouseLeftButtonDown`/`GroupEntryNameEdit_*` handlers already in `TrayPopupWindow.xaml.cs` — new handlers `GroupItemEntryName_MouseLeftButtonDown` etc. follow the same code-behind shape), `DetailText`, `RelativeTime`, up/down arrows, delete (`X`) button — smaller font (FontSize 9-10) than the group row to keep the visual hierarchy clear (group row → its items, same relationship as section header → its groups).

---

## 7. Implementation Phases

- **Phase A — Data & service.** `ArchivedGroupEntryInfo`, `ClipboardGroup.Entries`, `RenameGroupEntryAsync`/`DeleteGroupEntryAsync`/`MoveGroupEntryAsync`, `ISettingsService.LoadGroupNameColumnWidth`/`SaveGroupNameColumnWidth`. Unit tests for each new service method (rename, delete-with-blob-removal, delete-last-item-deletes-group, reorder + boundary no-ops, persistence round-trip).
- **Phase B — View model.** `NameColumnWidth`, `GroupDisplayItem.IsExpanded`/`Entries`, `GroupEntryDisplayItem`, `AnyGroupEditing` wiring. View-model tests using mocked `IClipboardGroupService`/`ISettingsService` (pattern from `GroupsTabViewModelTests`).
- **Phase C — UI.** Splitter + shared width binding, expand chevron + nested item rows, click-to-rename handlers. Manual verification in the running app (no WPF UI test harness in this repo today) — specifically: drag the splitter and confirm every row's name column moves together and the width survives an app restart; expand a group, rename/delete/reorder an item, confirm `groups.json` and the blob file on disk both update; delete the last item and confirm the group disappears from the list.

Each phase should compile and pass tests before starting the next.

---

## 8. Testing Strategy

- `ClipboardGroupServiceTests`: `RenameGroupEntryAsync` updates `Entries[].Name` and persists; `DeleteGroupEntryAsync` removes the blob file from disk and shrinks `EntryIds`/`Entries`; `DeleteGroupEntryAsync` on a group's last item deletes the whole group (assert `Groups` no longer contains it and its archive folder is gone); `MoveGroupEntryAsync` boundary no-ops (mirroring existing `MoveGroupAsync`/`MoveFolderAsync` boundary tests); round-trip persistence (save, reload via a fresh `ClipboardGroupService` instance pointed at the same directory, confirm `Entries` metadata survives, not just `EntryIds`).
- `GroupsTabViewModelTests`: `Refresh()` populates each `GroupDisplayItem.Entries` from the service's `group.Entries`; `MoveUpCommand`/`MoveDownCommand` on items are null only at list boundaries (never gated by `GroupSortMode`, unlike the group-level arrows); `ToggleExpandCommand` flips `IsExpanded` without calling the service (pure UI state); `AnyGroupEditing` is true while an item's `IsEditing` is true.
- No test currently touches `NameColumnWidth`/`ArchivedGroupEntryInfo` (new surface) — confirm via `grep -r "NameColumnWidth\|ArchivedGroupEntryInfo" tests/` before assuming no fixture updates are needed elsewhere.

---

## 9. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| `ClipboardGroup.Entries` being `init`-only forces every mutator to rebuild the whole object instead of mutating in place | Same constraint already exists for `EntryIds`/`Name`/`FolderId` handling elsewhere in this file — no new pattern, just one more field to copy forward at each existing rebuild site. |
| Deleting an item's blob file races a concurrent export/restore of the same group | All three new mutators take `_gate`, same as every existing write; `ExportGroupToPackageAsync`/`RestoreGroupForIdAsync` don't currently take `_gate` for reads — pre-existing gap, out of scope for this plan, but worth flagging in the PR since it's adjacent. |
| Splitter binding path (`ElementName` + nested `DataContext.Path`) is easy to get subtly wrong in WPF and fail silently (no binding error dialog, just a static column) | Verify manually in Phase C by dragging and confirming *every* visible row's name column moves, not just the one dragged — a wrong binding path would silently fall back to per-row independent width, which looks correct until you scroll. |
| Auto-deleting a group when its last item is removed surprises a user who just wanted to prune one item and didn't realize it was the last one | The delete confirmation UX (out of scope here — no group/item delete in this app currently confirms) is unchanged; if this turns out to be a problem in practice, a "this was the last item, group deleted" toast/status-text update is a cheap follow-up (`StatusText` already exists on the view model). |

---

## 10. Success Criteria

- Dragging the splitter on any group row resizes the name column for every group row, and the width survives an app restart.
- Folder header name columns are unaffected.
- Expanding a group shows every one of its saved clips with name, type/size, and relative time.
- Renaming, deleting, and reordering an item inside a group all persist to `groups.json` and (for delete) remove the blob file, without touching the live clipboard or history.
- Deleting a group's last remaining item removes the group from the list entirely.
- Full existing test suite plus all new tests pass.

---
