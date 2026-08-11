# Plan: Groups Tab — Order-By Sorting & Collapsible Folders

**Status:** Draft
**Target version:** next patch after 1.14.18 (host-only change, no plugin API impact)
**Scope:** The Groups tab (saved clipboard groups) has grown unwieldy with no ordering control and no way to organize related groups together. Add an order-by control (Alphabetical / Item count / Date created / Custom) and user-defined, collapsible folders, both persisted across restarts.

---

WHEN IMPLEMENTING USE brutal-coder skill

AFTER IMPLEMENTATION use brutal-pr skill to review work and brutal-address-pr skill to address issues and loop until ALL issues, no matter how trivial are addressed

---

## 1. Problem Statement

`GroupsTabViewModel` renders `IClipboardGroupService.Groups` as a single flat list, always in whatever order `_groups` happens to hold in memory (newest saved/imported group inserted at index 0, per `ClipboardGroupService.SaveGroupAsync`/`ImportGroupFromPackageAsync`). There is no way to:

- Reorder groups by anything other than "when I saved it."
- Cluster related groups together — a user with a few dozen saved groups (the current install has 11) has no way to reduce what's on screen at once, or to separate e.g. "client X" clips from "scratch" clips.

This mirrors a real, reported pain point: "the groups are getting too much and disorganised."

## 2. Goals

### Must have

1. An order-by control with four modes: **Alphabetical** (name, case-insensitive), **Item count** (fewest/most clips — descending, i.e. biggest group first, matching "size" framing), **Date created** (newest first — today's implicit behavior), **Custom** (manual, via up/down arrows).
2. User-defined **folders**: create, rename, delete. A group belongs to at most one folder, or none ("Ungrouped").
3. Folders and the Ungrouped section are independently **collapsible**, and collapse state **persists** across restarts.
4. Order-by controls the order of groups **within** each folder/Ungrouped section, not folder order. Folder order is independently custom-orderable via its own up/down arrows, seeded by creation time.
5. Assign a group to a folder via **both** a "Move to folder…" picker (button/menu, keyboard-reachable, always available) and **drag-and-drop** onto a folder header (power-user shortcut). Both call the same service method.
6. Deleting a folder moves its groups to Ungrouped; it never deletes group data.
7. The Ungrouped section is always present, pinned above all folders.
8. Existing `groups.json` files (no `Folders`, no `FolderId`/`SortIndex` on groups) load losslessly: every existing group becomes Ungrouped, and its initial Custom-sort position matches today's on-disk order — no reshuffling on upgrade.
9. Tests for every new service method and the view-model's section-building/sort logic; no regression in the existing suite.

### Should have

10. Up/down arrows for groups (Custom mode) and folders are hidden unless their respective sort mode is Custom (groups) / always shown (folders — folder order has no non-custom mode other than "creation order," which the arrows override).
11. "New folder…" affordance discoverable from the Groups tab toolbar (next to the existing "Import group…" button).

### Won't have (this phase)

- Nested folders (folder-in-folder). One flat level only.
- Multi-folder membership (a group filed in more than one folder).
- Per-folder independent sort mode. One order-by setting applies everywhere.
- Search/filter within Groups tab.

---

## 3. Data Model

### 3.1 New model: `ClipboardGroupFolder`

```csharp
// src/Clipt/Models/ClipboardGroupFolder.cs
namespace Clipt.Models;

public sealed class ClipboardGroupFolder
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public required DateTime CreatedUtc { get; init; }
    public bool IsCollapsed { get; set; }

    /// <summary>Manual folder order (arrows always available). Seeded from CreatedUtc order.</summary>
    public int SortIndex { get; set; }
}
```

### 3.2 `ClipboardGroup` additions

```csharp
// src/Clipt/Models/ClipboardGroup.cs
public sealed class ClipboardGroup
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public required DateTime CreatedUtc { get; init; }
    public required IReadOnlyList<string> EntryIds { get; init; }

    public string? FolderId { get; set; }   // null = Ungrouped
    public int SortIndex { get; set; }      // used only when GroupSortMode.Custom
}
```

`ClipboardGroup` is currently constructed with object initializers everywhere (`SaveGroupAsync`, `ImportGroupFromPackageAsync`, `LoadAsync`) — `FolderId`/`SortIndex` are non-`required` so all existing call sites keep compiling; each call site is updated to set sensible defaults (`FolderId = null`, `SortIndex` = current `_groups.Count` at insert time, i.e. "goes to the end").

### 3.3 New enum: `GroupSortMode`

```csharp
// src/Clipt/Models/GroupSortMode.cs
namespace Clipt.Models;

public enum GroupSortMode
{
    DateCreated,     // default — matches today's behavior (newest first)
    Alphabetical,
    ItemCount,
    Custom,
}
```

### 3.4 Persistence format (`groups.json`, owned by `ClipboardGroupService`)

Extend `GroupsFileDto`:

```csharp
private sealed class GroupsFileDto
{
    public List<FolderDto> Folders { get; set; } = [];   // NEW
    public List<GroupDto> Groups { get; set; } = [];
}

private sealed class FolderDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public bool IsCollapsed { get; set; }
    public int SortIndex { get; set; }
}
```

`GroupDto` gains `FolderId` (nullable string) and `SortIndex` (int). Deserializing an old-format file (missing `Folders`, missing `FolderId`/`SortIndex` on `GroupDto`) yields `Folders = []` and `FolderId = null`/`SortIndex = 0` per `System.Text.Json` default-on-missing-property behavior — combined with the migration rule in 3.5, this satisfies Goal 8.

The Ungrouped section's collapse state is a single bool, not worth a folder row: persisted as a new registry value (3.6), not in `groups.json`.

### 3.5 Migration on load

In `ClipboardGroupService.LoadAsync`, after deserializing: if any loaded group has `SortIndex == 0` and more than one group shares that value (i.e., the file predates this feature and never had per-group `SortIndex` written), assign `SortIndex` from each group's position in the **existing on-disk array order** (0, 1, 2, …) before anything else touches `_groups`. This is a one-time, idempotent normalization — after the first save under the new format, every group has a distinct `SortIndex` and this branch never fires again for that file.

### 3.6 New settings (`ISettingsService` / `SettingsService`, registry-backed like `LogLevel`)

```csharp
GroupSortMode LoadGroupSortMode();          // HKCU\SOFTWARE\Clipt\GroupSortMode, REG_SZ, default DateCreated
void SaveGroupSortMode(GroupSortMode mode);

bool LoadGroupsUngroupedCollapsed();        // HKCU\SOFTWARE\Clipt\GroupsUngroupedCollapsed, REG_DWORD, default 0
void SaveGroupsUngroupedCollapsed(bool collapsed);
```

Follows the exact `Registry.CurrentUser.OpenSubKey`/`CreateSubKey` pattern already used for every other setting in `SettingsService.cs`.

---

## 4. Service Changes (`IClipboardGroupService` / `ClipboardGroupService`)

```csharp
public interface IClipboardGroupService
{
    IReadOnlyList<ClipboardGroup> Groups { get; }
    IReadOnlyList<ClipboardGroupFolder> Folders { get; }     // NEW

    // ... existing members unchanged ...

    Task CreateFolderAsync(string name);
    Task RenameFolderAsync(string folderId, string newName);
    /// <summary>Moves all groups in the folder to Ungrouped, then deletes the folder. Never deletes group data.</summary>
    Task DeleteFolderAsync(string folderId);
    Task SetFolderCollapsedAsync(string folderId, bool collapsed);
    /// <summary>direction: -1 toward index 0, +1 toward the end. No-op at a boundary or unknown id.</summary>
    Task MoveFolderAsync(string folderId, int direction);

    /// <summary>folderId null moves the group to Ungrouped. Used by both the picker button and drag-drop.</summary>
    Task MoveGroupToFolderAsync(string groupId, string? folderId);
    /// <summary>Reorders a group among its current folder/Ungrouped siblings only. direction: -1/+1.</summary>
    Task MoveGroupAsync(string groupId, int direction);
}
```

All mutating methods take `_gate`, mutate in-memory state, call `WriteGroupsFileAsync()` (extended to also serialize `Folders`), then raise the existing `GroupsChanged` event — no new event type. `MoveFolderAsync`/`MoveGroupAsync` mirror the existing adjacent-swap logic already implemented for history reordering in `ClipboardHistoryService.MoveAsync` (same file, established pattern — reuse the swap-by-index approach, not a full re-sort).

`DeleteFolderAsync` implementation note: iterate `_groups`, set `FolderId = null` for every group whose `FolderId` matches, then remove the folder from `_folders`. This never touches `EntryIds`/blobs — group data is untouched, only its folder assignment changes.

---

## 5. View Model Changes (`GroupsTabViewModel`)

Replace the flat `ObservableCollection<GroupDisplayItem> DisplayGroups` with a two-level structure:

```csharp
public ObservableCollection<GroupSectionDisplayItem> Sections { get; } = [];

public sealed partial class GroupSectionDisplayItem : ObservableObject
{
    public string? FolderId { get; init; }              // null = Ungrouped (always first, no move/rename/delete)
    public bool IsFolder => FolderId is not null;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private bool _isCollapsed;
    [ObservableProperty] private string _itemCountText = string.Empty;
    public ObservableCollection<GroupDisplayItem> Groups { get; } = [];

    public IAsyncRelayCommand? RenameCommand { get; init; }   // null for Ungrouped
    public IAsyncRelayCommand? DeleteCommand { get; init; }   // null for Ungrouped
    public IAsyncRelayCommand? MoveUpCommand { get; init; }   // null for Ungrouped and top folder
    public IAsyncRelayCommand? MoveDownCommand { get; init; } // null for Ungrouped and bottom folder
    public required IRelayCommand ToggleCollapsedCommand { get; init; }
}
```

`Refresh()` rebuild order:

1. Read `GroupSortMode` from settings once.
2. Build the Ungrouped section first (groups with `FolderId == null`), collapse state from `LoadGroupsUngroupedCollapsed()`.
3. Build one section per folder, in `Folders` ordered by `SortIndex`, collapse state from `folder.IsCollapsed`.
4. Within each section, sort its groups per the current `GroupSortMode`:
   - `DateCreated` → `CreatedUtc` descending (today's behavior).
   - `Alphabetical` → `Name`, `StringComparer.OrdinalIgnoreCase`.
   - `ItemCount` → `EntryIds.Count` descending.
   - `Custom` → `SortIndex` ascending.
5. Each `GroupDisplayItem` gains:
   - `MoveToFolderCommand` — opens a lightweight picker (existing folders + "New folder…"), calls `MoveGroupToFolderAsync`.
   - `MoveUpCommand`/`MoveDownCommand` — non-null only when `GroupSortMode == Custom`, mirroring `ShowMoveControls` gating already used in `HistoryTabViewModel`.
   - Drag source state — see §6.

`GroupSortMode` becomes an `[ObservableProperty]` on the view model bound to a `ComboBox`; its setter persists via settings and calls `Refresh()` (re-sorts within sections only — section order/collapse state untouched).

---

## 6. UI Changes (`TrayPopupWindow.xaml` / `.xaml.cs`)

- Groups tab toolbar: add a `ComboBox` bound to `GroupSortMode` next to the existing "Import group…" button, plus a "New folder…" button.
- Replace the single group `ItemsControl` with a nested structure: outer `ItemsControl ItemsSource="{Binding Sections}"`, each section rendered as a header row (chevron/`ToggleCollapsedCommand`, name, item count, and — only when `IsFolder` — Rename, Delete, and (when Custom… no, folder move is always available) Move up/down) followed by an inner `ItemsControl ItemsSource="{Binding Groups}" Visibility="{Binding IsCollapsed, Converter=..., ConverterParameter=Invert}"` reusing the existing group-row `DataTemplate`, extended with a "Move to folder…" button and conditional up/down arrows.
- Drag-and-drop, implemented in code-behind (`TrayPopupWindow.xaml.cs`), not the view model (WPF drag mechanics are a view concern):
  - `PreviewMouseLeftButtonDown` + `MouseMove` past a drag threshold on a group row → `DragDrop.DoDragDrop(row, groupDisplayItem, DragDropEffects.Move)`.
  - `DragOver`/`Drop` on a section header → if the dragged payload is a `GroupDisplayItem`, call `GroupsTab.MoveGroupToFolderCommand.Execute((groupId, targetFolderId))` (a new single command taking a tuple, or two params via a small relay wrapper) and set visual drop-target feedback (background highlight) during `DragEnter`/`DragLeave`.
  - Dropping a group back onto its own current section is a no-op (checked before calling the service, to avoid a redundant `GroupsChanged` refresh).

---

## 7. Implementation Phases

- **Phase A — Data & service.** `ClipboardGroupFolder`, `GroupSortMode`, `ClipboardGroup` additions, `groups.json` DTO changes + migration, all new `IClipboardGroupService` methods, new `SettingsService` members. Unit tests for every method (create/rename/delete folder, move group in/out, reorder folder, reorder group, migration of an old-format file).
- **Phase B — View model.** `GroupSectionDisplayItem`, `Sections` rebuild, sort-mode application, move/rename/delete/collapse commands wired to the service. View-model tests using mocked `IClipboardGroupService`/`ISettingsService` (pattern from `HistoryTabViewModelTests`).
- **Phase C — UI.** XAML restructure, order-by combo, new-folder button, move-to-folder picker, up/down arrows. Manual verification in the running app (no WPF UI test harness in this repo today).
- **Phase D — Drag-and-drop.** Code-behind drag source/drop target on top of the Phase C structure, reusing the Phase A `MoveGroupToFolderAsync`.

Each phase should compile and pass tests before starting the next; Phase D is additive and can ship slightly behind C if it needs more iteration.

---

## 8. Testing Strategy

- `ClipboardGroupServiceTests` (existing file): folder CRUD, `MoveGroupToFolderAsync` (including moving to null/Ungrouped), `DeleteFolderAsync` preserves group data and reassigns `FolderId`, `MoveFolderAsync`/`MoveGroupAsync` boundary no-ops (mirroring existing `MoveAsync_AtTopBoundary_NoOp` style tests on `ClipboardHistoryServiceTests`), round-trip persistence including an old-format fixture file with no `Folders`/`FolderId` to prove the migration path.
- New `GroupsTabViewModelTests`: section build order (Ungrouped first, then folders by `SortIndex`), sort mode applied per section independently of section order, `MoveUpCommand`/`MoveDownCommand` null unless `GroupSortMode.Custom`, folder move commands null at boundaries.
- No existing test relies on `DisplayGroups` (confirm via `grep DisplayGroups tests/` before deleting the property) — if any UI/other code still references the old flat collection, keep it as a computed `IEnumerable<GroupDisplayItem>` flattening `Sections` for compatibility rather than breaking callers silently.

---

## 9. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| Migration mis-orders existing 11 saved groups on first load | `SortIndex` seeded strictly from on-disk array order (§3.5), not re-derived from `CreatedUtc` (avoids reordering if timestamps are equal/out of insertion order). |
| Drag-and-drop is fiddly in WPF and could destabilize the Groups tab UI | Phase D is isolated and additive; the button-based "Move to folder…" path (Phase C) is fully functional without it, so drag-drop can slip without blocking the rest. |
| `groups.json` write races with the existing group save/import/export paths | No new locking needed — every new mutator goes through the same `_gate` `SemaphoreSlim` already guarding all `ClipboardGroupService` writes. |
| Deleting a folder while it's the target of an in-flight drag | `DeleteFolderAsync` and `MoveGroupToFolderAsync` both take `_gate`; a drop that races a delete either lands in the (still-existing) folder or falls through to Ungrouped once the folder's gone — no crash, no data loss either way. |

---

## 10. Open Questions (resolve during Phase C)

- Exact visual treatment for the drop-target highlight (color/border) — defer to whatever `Themes/` already defines for hover states, to avoid inventing a new brush.
- Whether "New folder…" should immediately enter rename/edit mode on the new folder's header (consistent with how new groups are *not* auto-renamed today) — lean yes, for discoverability, but confirm against feel once Phase C is up.

---

## 11. Success Criteria

- Order-by combo changes visibly reorder groups within every section immediately, and the choice survives an app restart.
- Creating, renaming, deleting a folder, and moving a group in/out of it (via both the button and drag-and-drop) all persist across restart.
- Collapse state per folder and for Ungrouped persists across restart.
- An 11-group pre-existing `groups.json` (matching this machine's real file) loads with all 11 groups in Ungrouped, in their current order, with zero manual re-filing needed to get back to today's behavior.
- Full existing test suite plus all new tests pass.

---

## 12. Implementation Note (deviation from this plan)

The shipped implementation does **not** add `SortIndex` to `ClipboardGroup`/`ClipboardGroupFolder` as drafted in §3.1/§3.2. Instead, Custom order is the groups'/folders' position in the existing in-memory `List<T>` (`_groups`/`_folders` in `ClipboardGroupService`), matching the pattern `ClipboardHistoryService.MoveAsync` already uses for history reordering. `MoveGroupAsync`/`MoveFolderAsync` swap adjacent list entries directly; `WriteGroupsFileAsync` already preserves list order on serialize, so no extra migration step was needed for Custom order — §3.5's migration section is moot (there is no `SortIndex` to backfill; old files load with existing on-disk group order preserved automatically).

This is a pure internal simplification — it removes a field that could drift out of sync with actual list position, matches an established convention elsewhere in this codebase, and produces identical externally-observable behavior to what this plan specifies.
