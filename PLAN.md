# Plan: Export / import saved group for another PC (Clipt)

## Overview

Saved groups today live under `%LocalAppData%\Clipt\History\`: metadata in [`groups.json`](src/Clipt/Services/ClipboardGroupService.cs) (per-group `archivedEntries` plus `entryIds`), and binary clip payloads under `groups/{groupId}/blobs/{archivedEntryId}.bin`. [`ClipboardHistoryService`](src/Clipt/Services/ClipboardHistoryService.cs) materializes archived clips from that layout when restoring a group.

**Goal:** One portable file a user can copy to another machine and **import** into Clipt so the group appears in the Groups tab with full restore behavior—**without** relying on the source PC’s live history or `index.json`.

**Risk (“one shot”):** Corrupting `groups.json` or leaving half-written blobs on failure. Imports and exports must be **atomic** where the product already uses temp-then-rename for JSON; blob writes should complete in a staging area before updating `groups.json` and only then become visible (same pattern as existing group save).

**Mandatory execution (exact wording):**

WHEN IMPLEMENTING USE brutal-coder skill

AFTER IMPLEMENTATION use brutal-pr skill to review work and brutal-address-pr skill to address issues and loop until ALL issues, no matter how trivial are addressed

---

## File format (export package)

Recommend a **single ZIP** archive (e.g. extension `.cliptgroup` or `.clipt-export`) so users move one file:

| Entry | Purpose |
|--------|--------|
| `manifest.json` | Versioned metadata + one group’s `archivedEntries` list (same logical fields as internal [`ArchivedGroupEntryDto`](src/Clipt/Services/ClipboardGroupService.cs) / history service duplicate) |
| `blobs/{archivedEntryId}.bin` | Raw blob bytes; filename **must** match `archivedEntries[].id` in the manifest |

**Manifest requirements:**

- `formatVersion` (integer, start at `1`) for forward compatibility checks.
- `exportedUtc` (ISO-8601).
- `exporterAppVersion` (string, from assembly—helps support/debug).
- `group`: `name`, `createdUtc` (preserve from source for display), `archivedEntries` array (full metadata per entry so `ContentType`, hash, summary, etc. survive).

**Explicit non-goals for v1:** Encrypting the package; cloud sync.

---

## Export flow

1. **Trigger:** Groups tab—per-group action (e.g. menu or button) “Export group…” bound to `SaveFileDialog` (filter: Clipt group `*.cliptgroup` or chosen extension).
2. **Service API:** e.g. `IClipboardGroupService.ExportGroupAsync(string groupId, string filePath, CancellationToken)` (exact name to match codebase conventions).
3. **Data source:** Under the service’s existing lock, resolve the group:
   - Confirm `groupId` exists in `_groups`.
   - Load **archived entry metadata** from the current `groups.json` on disk (today full `ArchivedGroupEntryDto` is **not** held on [`ClipboardGroup`](src/Clipt/Models/ClipboardGroup.cs)—only `EntryIds`). Deserializing the existing file under lock matches how [`WriteGroupsFileAsync`](src/Clipt/Services/ClipboardGroupService.cs) merges state.
4. **Validation:** For each archived entry, require `groups/{groupId}/blobs/{id}.bin` to exist; if any missing, fail the export with a clear error (no partial zip).
5. **Write:** Build ZIP in memory or via temp file on disk, then move/rename to the user path (avoid truncated files if the process dies mid-write).

---

## Import flow

1. **Trigger:** Groups tab toolbar or menu “Import group…” → `OpenFileDialog`.
2. **Service API:** e.g. `IClipboardGroupService.ImportGroupFromPackageAsync(string filePath, CancellationToken)` returning a result type (`Success`, `Failure` with user-visible message).
3. **Read & validate:**
   - Open ZIP; read and deserialize `manifest.json` with [`CliptJsonOptions.Shared`](src/Clipt/Services/CliptJsonOptions.cs) (or shared options for enums like `ContentType`).
   - Reject unknown `formatVersion` (policy: only `1` until a migration story exists).
   - Every `archivedEntries[]` entry must have a matching `blobs/{id}.bin`; verify sizes if manifest includes `dataSizeBytes` (optional integrity check).
4. **Remap IDs (critical):** On the target PC, generate a **new** `groupId` and **new** archived entry IDs (`Guid.NewGuid().ToString("N")`) so imports never collide with existing groups or archived IDs. Rewrite blob paths in the written archive directory to `{newId}.bin`.
5. **Atomic apply:**
   - Write all blobs under `groups/{newGroupId}/blobs/` (create dirs as today).
   - Update in-memory `_groups` and persist via the same **temp-then-move** pattern as [`WriteGroupsFileAsync`](src/Clipt/Services/ClipboardGroupService.cs), passing the new group’s `archivedEntries` into the merge dictionary (same path as `SaveGroupAsync`).
   - On any failure after partial writes: best effort delete the new `groups/{newGroupId}` folder if created; do not commit a half-updated `groups.json` (transactional intent: either full success or no new group row + no orphan folder).
6. **UI:** Fire `GroupsChanged` / refresh so [`GroupsTabViewModel`](src/Clipt/ViewModels/GroupsTabViewModel.cs) updates; show success or error (MessageBox or existing status pattern).

---

## Code touchpoints (expected)

| Area | Change |
|------|--------|
| [`IClipboardGroupService`](src/Clipt/Services/IClipboardGroupService.cs) | `ExportGroupAsync`, `ImportGroupFromPackageAsync` (or equivalent names) |
| [`ClipboardGroupService`](src/Clipt/Services/ClipboardGroupService.cs) | Implement export/import; possibly small helper to read `GroupsFileDto` slice for one group |
| [`GroupsTabViewModel`](src/Clipt/ViewModels/GroupsTabViewModel.cs) / [`GroupDisplayItem`](src/Clipt/ViewModels/GroupsTabViewModel.cs) | Commands + optional `ExportCommand`; `ImportGroupCommand` on tab |
| [`TrayPopupWindow.xaml`](src/Clipt/Views/TrayPopupWindow.xaml) | UI: export on row; import in Groups tab header row |
| [`App.xaml.cs`](src/Clipt/App.xaml.cs) | DI unchanged if service interface only grows |
| [`Clipt.Tests`](tests/Clipt.Tests) | New tests: export produces valid zip; import round-trip in isolated temp history dir; missing blob / bad manifest fail safely; mocks where IFileDialog is awkward—test service with temp paths |

**DTO drift:** [`ClipboardHistoryService`](src/Clipt/Services/ClipboardHistoryService.cs) duplicates group DTOs. Prefer **one** public/internal shared model for manifest serialization to avoid export/import/history divergence (small refactor acceptable if it reduces bug risk).

---

## Versioning (on implementation)

Per workspace rules: bump [`src/Clipt/Clipt.csproj`](src/Clipt/Clipt.csproj) `<Version>` and [`installer/Clipt.iss`](installer/Clipt.iss) `#define MyAppVersion` **first**, then implement.

---

## TESTS ON NEW FUNCTIONALITY USING MOCKS WHERE REQUIRED

- **Export:** Given a history dir with a valid group + blobs, assert ZIP contains `manifest.json`, correct `formatVersion`, and N blob entries; assert manifest entry IDs match zip paths.
- **Import:** Export then import into a **second** empty temp history root; assert new `groupId` ≠ original; assert `groups.json` lists the group and blob count matches; optional: call history restore path with new entry IDs if test harness allows.
- **Failure:** Remove one blob from zip or corrupt manifest → import returns failure and does not append a broken group (assert `groups.json` entry count unchanged when failure is after validation).

---

## TODO list (execution order)

1. Bump version in `Clipt.csproj` and `Clipt.iss`.
2. Define manifest DTOs + `formatVersion`; implement `ExportGroupAsync` with atomic zip write.
3. Implement `ImportGroupFromPackageAsync` with ID remapping and atomic persist.
4. Wire UI (export per group, import on Groups tab) and user-visible errors.
5. Add unit/integration tests under `Clipt.Tests` (temp directories; mocks for dialog-only code if split).
6. **brutal-pr** — Run brutal-pr skill to review work.
7. **brutal-address-pr** — Use brutal-address-pr skill to address all issues from review.
8. **brutal-pr-review-loop** — Use brutal-pr-review-loop skill to confirm the review loop completed properly.

---

## Summary

Ship a **versioned ZIP** with `manifest.json` + `blobs/*.bin`, export built only from consistent on-disk group state, import that **remaps all IDs** and applies blobs + `groups.json` in a failure-safe order. Follow the mandatory brutal execution and review block above and the TODO list through **brutal-pr-review-loop**.
