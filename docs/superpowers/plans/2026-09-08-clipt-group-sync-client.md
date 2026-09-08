# Clipt Group Sync — Client Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `GroupSyncService` to Clipt that end-to-end encrypts saved Groups and syncs them against the server built in `docs/superpowers/plans/2026-09-08-clipt-group-sync-server.md`, per `docs/superpowers/specs/2026-09-08-group-sync-design.md`. Talks to the server purely over the HTTP contract that plan already defines — no server code or VPS access needed to build or test this.

**Architecture:** A new `Clipt.Services.Sync` namespace, entirely additive to the existing Groups architecture. `GroupSyncService` subscribes to `IClipboardGroupService.GroupsChanged` and diffs against a small locally-persisted `sync-state.json` (id → last-synced server version + content hash) to find what's dirty — no existing Groups call site needs to know sync exists. The one exception is a single new method, `IClipboardGroupService.ApplyRemoteGroupAsync`, needed so a decrypted group pulled from the server can be written into `groups.json`/blobs using the existing storage code rather than duplicating it.

**Tech Stack:** .NET 8 WPF (existing), `Konscious.Security.Cryptography.Argon2` (Argon2id KDF), `System.Security.Cryptography.AesGcm` (built-in, AES-256-GCM), `System.Net.Http.Json` (built-in HTTP client), CommunityToolkit.Mvvm (existing), xUnit + Moq (existing test stack).

## Global Constraints

- The server is opaque bytes only: every group is serialized to a `GroupSyncEnvelope`, encrypted with AES-256-GCM under a key derived via Argon2id from the user's passphrase, before anything leaves the device.
- The passphrase itself is **never persisted** — not to the registry, not to disk, in any form, derived or otherwise. It lives only in the in-memory derived key for the lifetime of the running process. The server URL and bearer token *are* persisted (registry, matching every other Clipt setting) — they gate access, not confidentiality.
- Folders are **not synced** — only a group's own `FolderId` travels inside its encrypted envelope. If the referenced folder doesn't exist locally on the receiving device, the group falls back to Ungrouped. This is a deliberate, documented scope boundary (see design spec's "Groups only" scope), not a bug.
- Sync is strictly additive: every existing Groups code path (`ClipboardGroupService`, `GroupsTabViewModel`, all four plugins that touch groups) is untouched except the one new interface method below. Clipt must work identically with sync never configured.
- Known accepted limitation: if a group is deleted on one device while it has an *unpushed local edit* on another, that edit is not specially reconciled — it resolves naturally on that group's next push/conflict cycle rather than through purpose-built merge logic. Documented, not engineered around (single user, small number of devices, rare in practice).
- All new NuGet package references are exact-pinned versions, not floating ranges — `Konscious.Security.Cryptography.Argon2` `1.3.1`.

---

## File Structure

```
src/Clipt/Services/Sync/
  IGroupSyncCrypto.cs / GroupSyncCrypto.cs              Argon2id KDF + AES-256-GCM
  GroupSyncEnvelope.cs                                   plaintext envelope DTOs
  GroupSyncEnvelopeConverter.cs                          Envelope <-> ClipboardGroup, hashing
  GroupSyncState.cs                                       sync-state.json model
  IGroupSyncStateStore.cs / GroupSyncStateStore.cs        load/save sync-state.json
  IGroupSyncApiClient.cs / GroupSyncApiClient.cs           HTTP client for the 4 server endpoints
  IGroupEntryBlobReader.cs / GroupEntryBlobReader.cs        reads an archived entry's blob bytes from disk
  IGroupSyncService.cs / GroupSyncService.cs                orchestrator (debounce, reconcile, pull)
  GroupSyncSetupWindow.xaml / .xaml.cs                       setup dialog (server URL/token/passphrase)

src/Clipt/Services/IClipboardGroupService.cs             + ApplyRemoteGroupAsync
src/Clipt/Services/ClipboardGroupService.cs               + ApplyRemoteGroupAsync implementation
src/Clipt/Services/ISettingsService.cs                    + sync server URL/token/enabled
src/Clipt/Services/SettingsService.cs                      + sync server URL/token/enabled
src/Clipt/Views/TrayPopupWindow.xaml                      + "Sync…" menu item
src/Clipt/Views/TrayPopupWindow.xaml.cs                    + click handler, + pull-on-open hook
src/Clipt/App.xaml.cs                                     + DI registrations, startup hook

tests/Clipt.Tests/Services/Sync/
  GroupSyncCryptoTests.cs
  GroupSyncEnvelopeConverterTests.cs
  GroupSyncStateStoreTests.cs
  GroupSyncApiClientTests.cs
  GroupSyncServiceTests.cs
tests/Clipt.Tests/Services/ClipboardGroupServiceTests.cs   + ApplyRemoteGroupAsync tests
tests/Clipt.Tests/Services/SettingsServiceTests.cs         + sync setting round-trip tests
```

---

### Task 1: Crypto — Argon2id key derivation + AES-256-GCM

**Files:**
- Modify: `src/Clipt/Clipt.csproj` (add package reference)
- Create: `src/Clipt/Services/Sync/IGroupSyncCrypto.cs`
- Create: `src/Clipt/Services/Sync/GroupSyncCrypto.cs`
- Test: `tests/Clipt.Tests/Services/Sync/GroupSyncCryptoTests.cs`

**Interfaces:**
- Produces: `IGroupSyncCrypto.DeriveKey(string, byte[], int, int, int) -> byte[]`, `.Encrypt(byte[], byte[]) -> byte[]`, `.Decrypt(byte[], byte[]) -> byte[]`.

- [ ] **Step 1: Add the Argon2id package reference**

In `src/Clipt/Clipt.csproj`, inside the existing `<ItemGroup>` with `CommunityToolkit.Mvvm`/`Microsoft.Extensions.DependencyInjection`:
```xml
<PackageReference Include="Konscious.Security.Cryptography.Argon2" Version="1.3.1" />
```

- [ ] **Step 2: Write the failing tests**

`tests/Clipt.Tests/Services/Sync/GroupSyncCryptoTests.cs`:
```csharp
using Clipt.Services.Sync;

namespace Clipt.Tests.Services.Sync;

public class GroupSyncCryptoTests
{
    // Small params — this test only checks determinism/uniqueness, not production KDF strength.
    private const int TestTimeCost = 1;
    private const int TestMemoryCostKib = 8192;
    private const int TestParallelism = 1;

    private readonly GroupSyncCrypto _crypto = new();

    [Fact]
    public void DeriveKey_SamePassphraseAndSalt_ReturnsSameKey()
    {
        byte[] salt = [1, 2, 3, 4, 5, 6, 7, 8];

        byte[] key1 = _crypto.DeriveKey("correct horse battery staple", salt, TestTimeCost, TestMemoryCostKib, TestParallelism);
        byte[] key2 = _crypto.DeriveKey("correct horse battery staple", salt, TestTimeCost, TestMemoryCostKib, TestParallelism);

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void DeriveKey_DifferentPassphrase_ReturnsDifferentKey()
    {
        byte[] salt = [1, 2, 3, 4, 5, 6, 7, 8];

        byte[] key1 = _crypto.DeriveKey("passphrase one", salt, TestTimeCost, TestMemoryCostKib, TestParallelism);
        byte[] key2 = _crypto.DeriveKey("passphrase two", salt, TestTimeCost, TestMemoryCostKib, TestParallelism);

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void EncryptThenDecrypt_RoundTrips()
    {
        byte[] key = _crypto.DeriveKey("passphrase", [1, 2, 3, 4, 5, 6, 7, 8], TestTimeCost, TestMemoryCostKib, TestParallelism);
        byte[] plaintext = System.Text.Encoding.UTF8.GetBytes("hello, synced group");

        byte[] ciphertext = _crypto.Encrypt(key, plaintext);
        byte[] decrypted = _crypto.Decrypt(key, ciphertext);

        Assert.Equal(plaintext, decrypted);
        Assert.NotEqual(plaintext, ciphertext);
    }

    [Fact]
    public void Decrypt_TamperedPayload_ThrowsCryptographicException()
    {
        byte[] key = _crypto.DeriveKey("passphrase", [1, 2, 3, 4, 5, 6, 7, 8], TestTimeCost, TestMemoryCostKib, TestParallelism);
        byte[] ciphertext = _crypto.Encrypt(key, System.Text.Encoding.UTF8.GetBytes("hello"));
        ciphertext[^1] ^= 0xFF; // flip a bit in the auth tag

        Assert.Throws<System.Security.Cryptography.CryptographicException>(() => _crypto.Decrypt(key, ciphertext));
    }

    [Fact]
    public void Decrypt_WrongKey_ThrowsCryptographicException()
    {
        byte[] key1 = _crypto.DeriveKey("passphrase one", [1, 2, 3, 4, 5, 6, 7, 8], TestTimeCost, TestMemoryCostKib, TestParallelism);
        byte[] key2 = _crypto.DeriveKey("passphrase two", [1, 2, 3, 4, 5, 6, 7, 8], TestTimeCost, TestMemoryCostKib, TestParallelism);
        byte[] ciphertext = _crypto.Encrypt(key1, System.Text.Encoding.UTF8.GetBytes("hello"));

        Assert.Throws<System.Security.Cryptography.CryptographicException>(() => _crypto.Decrypt(key2, ciphertext));
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run (from repo root, via VS2022 MSBuild + vstest per this project's WSL build recipe): build `tests/Clipt.Tests/Clipt.Tests.csproj`.
Expected: build FAILS — `GroupSyncCrypto` does not exist.

- [ ] **Step 4: Implement `IGroupSyncCrypto.cs`**

```csharp
namespace Clipt.Services.Sync;

public interface IGroupSyncCrypto
{
    byte[] DeriveKey(string passphrase, byte[] salt, int timeCost, int memoryCostKib, int parallelism);

    /// <summary>Returns nonce (12 bytes) + ciphertext + tag (16 bytes), concatenated.</summary>
    byte[] Encrypt(byte[] key, byte[] plaintext);

    /// <summary>Reverses <see cref="Encrypt"/>. Throws <see cref="System.Security.Cryptography.CryptographicException"/> if the payload was tampered with or the key is wrong.</summary>
    byte[] Decrypt(byte[] key, byte[] payload);
}
```

- [ ] **Step 5: Implement `GroupSyncCrypto.cs`**

```csharp
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Clipt.Services.Sync;

public sealed class GroupSyncCrypto : IGroupSyncCrypto
{
    private const int KeySizeBytes = 32;
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    public byte[] DeriveKey(string passphrase, byte[] salt, int timeCost, int memoryCostKib, int parallelism)
    {
        ArgumentException.ThrowIfNullOrEmpty(passphrase);
        ArgumentNullException.ThrowIfNull(salt);

        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(passphrase))
        {
            Salt = salt,
            DegreeOfParallelism = parallelism,
            Iterations = timeCost,
            MemorySize = memoryCostKib,
        };
        return argon2.GetBytes(KeySizeBytes);
    }

    public byte[] Encrypt(byte[] key, byte[] plaintext)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(plaintext);

        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[TagSizeBytes];

        using (var aes = new AesGcm(key, TagSizeBytes))
            aes.Encrypt(nonce, plaintext, ciphertext, tag);

        byte[] result = new byte[NonceSizeBytes + ciphertext.Length + TagSizeBytes];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSizeBytes);
        Buffer.BlockCopy(ciphertext, 0, result, NonceSizeBytes, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, result, NonceSizeBytes + ciphertext.Length, TagSizeBytes);
        return result;
    }

    public byte[] Decrypt(byte[] key, byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Length < NonceSizeBytes + TagSizeBytes)
            throw new ArgumentException("Payload too short to be valid.", nameof(payload));

        byte[] nonce = payload[..NonceSizeBytes];
        byte[] tag = payload[^TagSizeBytes..];
        byte[] ciphertext = payload[NonceSizeBytes..^TagSizeBytes];
        byte[] plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, TagSizeBytes);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Expected: 5 passed.

- [ ] **Step 7: Commit**

```bash
git add src/Clipt/Clipt.csproj src/Clipt/Services/Sync/IGroupSyncCrypto.cs src/Clipt/Services/Sync/GroupSyncCrypto.cs tests/Clipt.Tests/Services/Sync/GroupSyncCryptoTests.cs
git commit -m "Clipt: Argon2id key derivation + AES-256-GCM for group sync"
```

---

### Task 2: Sync envelope — the plaintext shape that gets encrypted

**Files:**
- Create: `src/Clipt/Services/Sync/GroupSyncEnvelope.cs`
- Create: `src/Clipt/Services/Sync/GroupSyncEnvelopeConverter.cs`
- Test: `tests/Clipt.Tests/Services/Sync/GroupSyncEnvelopeConverterTests.cs`

**Interfaces:**
- Consumes: `Clipt.Models.ClipboardGroup`, `Clipt.Models.ArchivedGroupEntryInfo` (existing).
- Produces: `GroupSyncEnvelope`, `GroupSyncEnvelopeEntry`, and `GroupSyncEnvelopeConverter.{ToEnvelope, SerializeToBytes, DeserializeFromBytes, FromEnvelope, ComputeContentHash}` — all consumed by Task 8 (`GroupSyncService`).

- [ ] **Step 1: Write the failing tests**

`tests/Clipt.Tests/Services/Sync/GroupSyncEnvelopeConverterTests.cs`:
```csharp
using Clipt.Models;
using Clipt.Services.Sync;

namespace Clipt.Tests.Services.Sync;

public class GroupSyncEnvelopeConverterTests
{
    private static ClipboardGroup MakeGroup(string id = "g1", string? folderId = null) => new()
    {
        Id = id,
        Name = "My Group",
        CreatedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        FolderId = folderId,
        EntryIds = ["e1"],
        Entries =
        [
            new ArchivedGroupEntryInfo(
                Id: "e1",
                SourceEntryId: "src1",
                Name: "Clip One",
                TimestampUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                SequenceNumber: 1,
                OwnerProcess: "notepad",
                OwnerPid: 123,
                Summary: "Clip One",
                ContentType: ContentType.Text,
                DataSizeBytes: 5,
                ContentHash: "irrelevant-on-source-device"),
        ],
    };

    [Fact]
    public void ToEnvelope_BuildsExpectedEnvelope()
    {
        ClipboardGroup group = MakeGroup(folderId: "f1");
        var blobs = new Dictionary<string, byte[]> { ["e1"] = [1, 2, 3, 4, 5] };

        GroupSyncEnvelope envelope = GroupSyncEnvelopeConverter.ToEnvelope(group, blobs);

        Assert.Equal("My Group", envelope.Name);
        Assert.Equal("f1", envelope.FolderId);
        Assert.Single(envelope.Entries);
        Assert.Equal("e1", envelope.Entries[0].Id);
        Assert.Equal("Clip One", envelope.Entries[0].Name);
        Assert.Equal(Convert.ToBase64String([1, 2, 3, 4, 5]), envelope.Entries[0].BlobBase64);
    }

    [Fact]
    public void ToEnvelope_EntryMissingFromBlobDictionary_IsSkipped()
    {
        ClipboardGroup group = MakeGroup();
        var blobs = new Dictionary<string, byte[]>(); // e1's blob missing

        GroupSyncEnvelope envelope = GroupSyncEnvelopeConverter.ToEnvelope(group, blobs);

        Assert.Empty(envelope.Entries);
    }

    [Fact]
    public void SerializeThenDeserialize_RoundTrips()
    {
        GroupSyncEnvelope envelope = GroupSyncEnvelopeConverter.ToEnvelope(
            MakeGroup(folderId: "f1"), new Dictionary<string, byte[]> { ["e1"] = [9, 9, 9] });

        byte[] bytes = GroupSyncEnvelopeConverter.SerializeToBytes(envelope);
        GroupSyncEnvelope roundTripped = GroupSyncEnvelopeConverter.DeserializeFromBytes(bytes);

        Assert.Equal(envelope.Name, roundTripped.Name);
        Assert.Equal(envelope.FolderId, roundTripped.FolderId);
        Assert.Equal(envelope.Entries[0].BlobBase64, roundTripped.Entries[0].BlobBase64);
    }

    [Fact]
    public void FromEnvelope_ReconstructsEntriesAndBlobs()
    {
        GroupSyncEnvelope envelope = GroupSyncEnvelopeConverter.ToEnvelope(
            MakeGroup(), new Dictionary<string, byte[]> { ["e1"] = [7, 7, 7] });

        (List<ArchivedGroupEntryInfo> entries, Dictionary<string, byte[]> blobs) = GroupSyncEnvelopeConverter.FromEnvelope(envelope);

        Assert.Single(entries);
        Assert.Equal("e1", entries[0].Id);
        Assert.Equal("Clip One", entries[0].Name);
        Assert.Equal([7, 7, 7], blobs["e1"]);
    }

    [Fact]
    public void ComputeContentHash_SameContent_ProducesSameHash()
    {
        GroupSyncEnvelope envelope1 = GroupSyncEnvelopeConverter.ToEnvelope(
            MakeGroup(), new Dictionary<string, byte[]> { ["e1"] = [1, 2, 3] });
        GroupSyncEnvelope envelope2 = GroupSyncEnvelopeConverter.ToEnvelope(
            MakeGroup(), new Dictionary<string, byte[]> { ["e1"] = [1, 2, 3] });

        Assert.Equal(
            GroupSyncEnvelopeConverter.ComputeContentHash(envelope1),
            GroupSyncEnvelopeConverter.ComputeContentHash(envelope2));
    }

    [Fact]
    public void ComputeContentHash_DifferentContent_ProducesDifferentHash()
    {
        GroupSyncEnvelope envelope1 = GroupSyncEnvelopeConverter.ToEnvelope(
            MakeGroup(), new Dictionary<string, byte[]> { ["e1"] = [1, 2, 3] });
        GroupSyncEnvelope envelope2 = GroupSyncEnvelopeConverter.ToEnvelope(
            MakeGroup(), new Dictionary<string, byte[]> { ["e1"] = [9, 9, 9] });

        Assert.NotEqual(
            GroupSyncEnvelopeConverter.ComputeContentHash(envelope1),
            GroupSyncEnvelopeConverter.ComputeContentHash(envelope2));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Expected: build FAILS — `GroupSyncEnvelope`/`GroupSyncEnvelopeConverter` don't exist.

- [ ] **Step 3: Implement `GroupSyncEnvelope.cs`**

```csharp
using Clipt.Models;

namespace Clipt.Services.Sync;

/// <summary>
/// The plaintext JSON shape encrypted into one group's sync ciphertext. Deliberately narrower than
/// <see cref="ClipboardGroup"/>/<see cref="ArchivedGroupEntryInfo"/> — fields like SourceEntryId,
/// OwnerProcess, and SequenceNumber describe the originating device's local history and aren't
/// meaningful once synced elsewhere (mirrors what <c>ClipboardGroupService.ImportGroupFromPackageAsync</c>
/// already does for cross-device group transfer via .cliptgroup files).
/// </summary>
public sealed class GroupSyncEnvelope
{
    public required string Name { get; init; }
    public string? FolderId { get; init; }
    public required DateTime CreatedUtc { get; init; }
    public required List<GroupSyncEnvelopeEntry> Entries { get; init; }
}

public sealed class GroupSyncEnvelopeEntry
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required DateTime TimestampUtc { get; init; }
    public required ContentType ContentType { get; init; }
    public required string BlobBase64 { get; init; }
}
```

- [ ] **Step 4: Implement `GroupSyncEnvelopeConverter.cs`**

```csharp
using System.Security.Cryptography;
using System.Text.Json;
using Clipt.Models;

namespace Clipt.Services.Sync;

public static class GroupSyncEnvelopeConverter
{
    public static GroupSyncEnvelope ToEnvelope(ClipboardGroup group, IReadOnlyDictionary<string, byte[]> entryBlobs)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(entryBlobs);

        var entries = new List<GroupSyncEnvelopeEntry>(group.Entries.Count);
        foreach (ArchivedGroupEntryInfo entry in group.Entries)
        {
            if (!entryBlobs.TryGetValue(entry.Id, out byte[]? blob))
                continue;

            entries.Add(new GroupSyncEnvelopeEntry
            {
                Id = entry.Id,
                Name = entry.Name,
                TimestampUtc = entry.TimestampUtc,
                ContentType = entry.ContentType,
                BlobBase64 = Convert.ToBase64String(blob),
            });
        }

        return new GroupSyncEnvelope
        {
            Name = group.Name,
            FolderId = group.FolderId,
            CreatedUtc = group.CreatedUtc,
            Entries = entries,
        };
    }

    public static byte[] SerializeToBytes(GroupSyncEnvelope envelope) =>
        JsonSerializer.SerializeToUtf8Bytes(envelope);

    public static GroupSyncEnvelope DeserializeFromBytes(byte[] json) =>
        JsonSerializer.Deserialize<GroupSyncEnvelope>(json)
            ?? throw new InvalidOperationException("Decrypted envelope deserialized to null.");

    /// <summary>Reconstructs the entry list and raw blob bytes for <c>IClipboardGroupService.ApplyRemoteGroupAsync</c>.</summary>
    public static (List<ArchivedGroupEntryInfo> Entries, Dictionary<string, byte[]> Blobs) FromEnvelope(GroupSyncEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var entries = new List<ArchivedGroupEntryInfo>(envelope.Entries.Count);
        var blobs = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        foreach (GroupSyncEnvelopeEntry e in envelope.Entries)
        {
            byte[] blob = Convert.FromBase64String(e.BlobBase64);
            blobs[e.Id] = blob;
            entries.Add(new ArchivedGroupEntryInfo(
                Id: e.Id,
                SourceEntryId: string.Empty,
                Name: e.Name,
                TimestampUtc: e.TimestampUtc,
                SequenceNumber: 0,
                OwnerProcess: "(synced)",
                OwnerPid: 0,
                Summary: e.Name,
                ContentType: e.ContentType,
                DataSizeBytes: blob.LongLength,
                ContentHash: Convert.ToHexString(SHA256.HashData(blob))));
        }

        return (entries, blobs);
    }

    /// <summary>SHA-256 over the canonical envelope bytes — used to detect local changes since the last successful sync.</summary>
    public static string ComputeContentHash(GroupSyncEnvelope envelope) =>
        Convert.ToHexString(SHA256.HashData(SerializeToBytes(envelope)));
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Expected: 6 passed.

- [ ] **Step 6: Commit**

```bash
git add src/Clipt/Services/Sync/GroupSyncEnvelope.cs src/Clipt/Services/Sync/GroupSyncEnvelopeConverter.cs tests/Clipt.Tests/Services/Sync/GroupSyncEnvelopeConverterTests.cs
git commit -m "Clipt: group sync envelope model and converter"
```

---

### Task 3: Local sync state (`sync-state.json`)

**Files:**
- Create: `src/Clipt/Services/Sync/GroupSyncState.cs`
- Test: `tests/Clipt.Tests/Services/Sync/GroupSyncStateStoreTests.cs`

**Interfaces:**
- Produces: `GroupSyncState { long GlobalLastPulledVersion; Dictionary<string, GroupSyncStateEntry> Groups; }`, `GroupSyncStateEntry { int SyncedVersion; string ContentHash; }`, `IGroupSyncStateStore.{LoadAsync, SaveAsync}`, `GroupSyncStateStore` (default path `%LocalAppData%\Clipt\History\sync-state.json`; `internal` constructor taking an explicit path, for tests — same pattern as `ClipboardGroupService`'s `internal` test constructor).

- [ ] **Step 1: Write the failing tests**

`tests/Clipt.Tests/Services/Sync/GroupSyncStateStoreTests.cs`:
```csharp
using Clipt.Services.Sync;

namespace Clipt.Tests.Services.Sync;

public class GroupSyncStateStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _path;

    public GroupSyncStateStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CliptSyncStateTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _path = Path.Combine(_tempDir, "sync-state.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task LoadAsync_FileDoesNotExist_ReturnsEmptyState()
    {
        var store = new GroupSyncStateStore(_path);

        GroupSyncState state = await store.LoadAsync();

        Assert.Equal(0, state.GlobalLastPulledVersion);
        Assert.Empty(state.Groups);
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTrips()
    {
        var store = new GroupSyncStateStore(_path);
        var state = new GroupSyncState { GlobalLastPulledVersion = 42 };
        state.Groups["g1"] = new GroupSyncStateEntry { SyncedVersion = 3, ContentHash = "abc123" };

        await store.SaveAsync(state);
        GroupSyncState loaded = await store.LoadAsync();

        Assert.Equal(42, loaded.GlobalLastPulledVersion);
        Assert.Equal(3, loaded.Groups["g1"].SyncedVersion);
        Assert.Equal("abc123", loaded.Groups["g1"].ContentHash);
    }

    [Fact]
    public async Task LoadAsync_CorruptFile_ReturnsEmptyStateRatherThanThrowing()
    {
        await File.WriteAllTextAsync(_path, "{ not valid json");
        var store = new GroupSyncStateStore(_path);

        GroupSyncState state = await store.LoadAsync();

        Assert.Equal(0, state.GlobalLastPulledVersion);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Expected: build FAILS — `GroupSyncStateStore` doesn't exist.

- [ ] **Step 3: Implement `GroupSyncState.cs`**

```csharp
using System.IO;
using System.Text.Json;

namespace Clipt.Services.Sync;

public sealed class GroupSyncState
{
    public long GlobalLastPulledVersion { get; set; }
    public Dictionary<string, GroupSyncStateEntry> Groups { get; init; } = new(StringComparer.Ordinal);
}

public sealed class GroupSyncStateEntry
{
    public required int SyncedVersion { get; set; }
    public required string ContentHash { get; set; }
}

public interface IGroupSyncStateStore
{
    Task<GroupSyncState> LoadAsync();
    Task SaveAsync(GroupSyncState state);
}

public sealed class GroupSyncStateStore : IGroupSyncStateStore
{
    private readonly string _path;

    public GroupSyncStateStore()
        : this(GetDefaultPath())
    {
    }

    internal GroupSyncStateStore(string path)
    {
        _path = path;
    }

    public async Task<GroupSyncState> LoadAsync()
    {
        if (!File.Exists(_path))
            return new GroupSyncState();

        try
        {
            byte[] json = await File.ReadAllBytesAsync(_path).ConfigureAwait(false);
            return JsonSerializer.Deserialize<GroupSyncState>(json) ?? new GroupSyncState();
        }
        catch (IOException)
        {
            return new GroupSyncState();
        }
        catch (JsonException)
        {
            return new GroupSyncState();
        }
    }

    public async Task SaveAsync(GroupSyncState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(state);
        string tmpPath = _path + ".tmp";
        await File.WriteAllBytesAsync(tmpPath, json).ConfigureAwait(false);
        File.Move(tmpPath, _path, overwrite: true);
    }

    private static string GetDefaultPath()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Clipt", "History", "sync-state.json");
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Expected: 3 passed.

- [ ] **Step 5: Commit**

```bash
git add src/Clipt/Services/Sync/GroupSyncState.cs tests/Clipt.Tests/Services/Sync/GroupSyncStateStoreTests.cs
git commit -m "Clipt: local sync-state.json store"
```

---

### Task 4: Blob reader — reads an archived entry's bytes from disk

**Files:**
- Create: `src/Clipt/Services/Sync/IGroupEntryBlobReader.cs`

No dedicated test file for this task — it's a two-line file-path resolver over `File.ReadAllBytesAsync`, and its only real behavior (does it resolve the right path for a given group/entry id) is exercised indirectly by Task 8's `GroupSyncService` tests, which fake this interface rather than touch disk. Keeping it as its own file (not inlined into Task 8) is still correct because it's the seam that keeps `GroupSyncService` free of direct file I/O and therefore mockable.

**Interfaces:**
- Produces: `IGroupEntryBlobReader.ReadEntryBlobAsync(string groupId, string entryId) -> Task<byte[]>`, `GroupEntryBlobReader` (default path `%LocalAppData%\Clipt\History\groups\<groupId>\blobs\<entryId>.bin` — the same path `ClipboardGroupService` itself uses internally).

- [ ] **Step 1: Implement `IGroupEntryBlobReader.cs`**

```csharp
namespace Clipt.Services.Sync;

public interface IGroupEntryBlobReader
{
    Task<byte[]> ReadEntryBlobAsync(string groupId, string entryId);
}

public sealed class GroupEntryBlobReader : IGroupEntryBlobReader
{
    private readonly string _groupArchiveRootDirectory;

    public GroupEntryBlobReader()
        : this(GetDefaultGroupArchiveRootDirectory())
    {
    }

    internal GroupEntryBlobReader(string groupArchiveRootDirectory)
    {
        _groupArchiveRootDirectory = groupArchiveRootDirectory;
    }

    public Task<byte[]> ReadEntryBlobAsync(string groupId, string entryId)
    {
        string path = Path.Combine(_groupArchiveRootDirectory, groupId, "blobs", entryId + ".bin");
        return File.ReadAllBytesAsync(path);
    }

    private static string GetDefaultGroupArchiveRootDirectory()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Clipt", "History", "groups");
    }
}
```

- [ ] **Step 2: Build to verify it compiles**

Build `src/Clipt/Clipt.csproj`. Expected: builds clean.

- [ ] **Step 3: Commit**

```bash
git add src/Clipt/Services/Sync/IGroupEntryBlobReader.cs
git commit -m "Clipt: group entry blob reader for sync"
```

---

### Task 5: `ApplyRemoteGroupAsync` on `IClipboardGroupService`

**Files:**
- Modify: `src/Clipt/Services/IClipboardGroupService.cs`
- Modify: `src/Clipt/Services/ClipboardGroupService.cs`
- Test: `tests/Clipt.Tests/Services/ClipboardGroupServiceTests.cs`

**Interfaces:**
- Produces: `IClipboardGroupService.ApplyRemoteGroupAsync(string groupId, string name, string? folderId, DateTime createdUtc, IReadOnlyList<ArchivedGroupEntryInfo> entries, IReadOnlyDictionary<string, byte[]> entryBlobs) -> Task` — consumed by Task 8 (`GroupSyncService`).

- [ ] **Step 1: Write the failing tests**

Append to `tests/Clipt.Tests/Services/ClipboardGroupServiceTests.cs` (inside the existing `ClipboardGroupServiceTests` class, using its existing `CreateService()`/`_tempDir` helpers):
```csharp
[Fact]
public async Task ApplyRemoteGroupAsync_NewGroup_CreatesGroupAndWritesBlobs()
{
    var service = CreateService();
    await service.LoadAsync();

    var entries = new List<ArchivedGroupEntryInfo>
    {
        new(Id: "e1", SourceEntryId: "", Name: "Clip", TimestampUtc: DateTime.UtcNow,
            SequenceNumber: 0, OwnerProcess: "(synced)", OwnerPid: 0, Summary: "Clip",
            ContentType: ContentType.Text, DataSizeBytes: 3, ContentHash: "x"),
    };
    var blobs = new Dictionary<string, byte[]> { ["e1"] = [1, 2, 3] };

    await service.ApplyRemoteGroupAsync("g1", "Synced Group", null, DateTime.UtcNow, entries, blobs);

    ClipboardGroup group = Assert.Single(service.Groups);
    Assert.Equal("g1", group.Id);
    Assert.Equal("Synced Group", group.Name);
    string blobPath = Path.Combine(_tempDir, "groups", "g1", "blobs", "e1.bin");
    Assert.True(File.Exists(blobPath));
    Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(blobPath));
}

[Fact]
public async Task ApplyRemoteGroupAsync_ExistingGroupId_OverwritesInPlace()
{
    var service = CreateService();
    await service.LoadAsync();
    var entries = new List<ArchivedGroupEntryInfo>
    {
        new(Id: "e1", SourceEntryId: "", Name: "Original", TimestampUtc: DateTime.UtcNow,
            SequenceNumber: 0, OwnerProcess: "(synced)", OwnerPid: 0, Summary: "Original",
            ContentType: ContentType.Text, DataSizeBytes: 1, ContentHash: "x"),
    };
    await service.ApplyRemoteGroupAsync("g1", "Original Name", null, DateTime.UtcNow, entries, new Dictionary<string, byte[]> { ["e1"] = [1] });

    var updatedEntries = new List<ArchivedGroupEntryInfo>
    {
        new(Id: "e1", SourceEntryId: "", Name: "Updated", TimestampUtc: DateTime.UtcNow,
            SequenceNumber: 0, OwnerProcess: "(synced)", OwnerPid: 0, Summary: "Updated",
            ContentType: ContentType.Text, DataSizeBytes: 1, ContentHash: "y"),
    };
    await service.ApplyRemoteGroupAsync("g1", "Updated Name", null, DateTime.UtcNow, updatedEntries, new Dictionary<string, byte[]> { ["e1"] = [2] });

    ClipboardGroup group = Assert.Single(service.Groups);
    Assert.Equal("Updated Name", group.Name);
    Assert.Equal("Updated", group.Entries[0].Name);
}

[Fact]
public async Task ApplyRemoteGroupAsync_UnknownFolderId_FallsBackToUngrouped()
{
    var service = CreateService();
    await service.LoadAsync();
    var entries = new List<ArchivedGroupEntryInfo>
    {
        new(Id: "e1", SourceEntryId: "", Name: "Clip", TimestampUtc: DateTime.UtcNow,
            SequenceNumber: 0, OwnerProcess: "(synced)", OwnerPid: 0, Summary: "Clip",
            ContentType: ContentType.Text, DataSizeBytes: 1, ContentHash: "x"),
    };

    await service.ApplyRemoteGroupAsync("g1", "Synced Group", "unknown-folder-id", DateTime.UtcNow, entries, new Dictionary<string, byte[]> { ["e1"] = [1] });

    Assert.Null(service.Groups[0].FolderId);
}

[Fact]
public async Task ApplyRemoteGroupAsync_RaisesGroupsChanged()
{
    var service = CreateService();
    await service.LoadAsync();
    bool raised = false;
    service.GroupsChanged += (_, _) => raised = true;
    var entries = new List<ArchivedGroupEntryInfo>
    {
        new(Id: "e1", SourceEntryId: "", Name: "Clip", TimestampUtc: DateTime.UtcNow,
            SequenceNumber: 0, OwnerProcess: "(synced)", OwnerPid: 0, Summary: "Clip",
            ContentType: ContentType.Text, DataSizeBytes: 1, ContentHash: "x"),
    };

    await service.ApplyRemoteGroupAsync("g1", "Synced Group", null, DateTime.UtcNow, entries, new Dictionary<string, byte[]> { ["e1"] = [1] });

    Assert.True(raised);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Expected: build FAILS — `ApplyRemoteGroupAsync` doesn't exist on `ClipboardGroupService`/`IClipboardGroupService`.

- [ ] **Step 3: Add the method to `IClipboardGroupService.cs`**

Add after `MoveGroupEntryAsync`:
```csharp
/// <summary>
/// Upserts a group by id from externally-provided content (name, folder, entries, blob bytes) rather
/// than resolving entries from the live history index — used only by group sync to materialize a group
/// pulled from the server. Overwrites any existing local group with the same id. If <paramref name="folderId"/>
/// doesn't match a folder that exists locally, the group is filed under Ungrouped instead (folders
/// themselves are not synced — only a group's own content and its folder assignment, when that folder
/// happens to already exist on this device).
/// </summary>
Task ApplyRemoteGroupAsync(
    string groupId,
    string name,
    string? folderId,
    DateTime createdUtc,
    IReadOnlyList<ArchivedGroupEntryInfo> entries,
    IReadOnlyDictionary<string, byte[]> entryBlobs);
```

- [ ] **Step 4: Implement it in `ClipboardGroupService.cs`**

Add after `MoveGroupEntryAsync`:
```csharp
public async Task ApplyRemoteGroupAsync(
    string groupId,
    string name,
    string? folderId,
    DateTime createdUtc,
    IReadOnlyList<ArchivedGroupEntryInfo> entries,
    IReadOnlyDictionary<string, byte[]> entryBlobs)
{
    ArgumentException.ThrowIfNullOrEmpty(groupId);
    ArgumentNullException.ThrowIfNull(entries);
    ArgumentNullException.ThrowIfNull(entryBlobs);

    string trimmedName = string.IsNullOrWhiteSpace(name) ? "Untitled" : name.Trim();

    await _gate.WaitAsync().ConfigureAwait(false);
    try
    {
        string? resolvedFolderId = folderId is { Length: > 0 } && _folders.Any(f => f.Id == folderId)
            ? folderId
            : null;

        string blobRoot = Path.Combine(_groupArchiveRootDirectory, groupId, "blobs");
        Directory.CreateDirectory(blobRoot);
        foreach ((string entryId, byte[] blob) in entryBlobs)
            await File.WriteAllBytesAsync(Path.Combine(blobRoot, entryId + ".bin"), blob).ConfigureAwait(false);

        var group = new ClipboardGroup
        {
            Id = groupId,
            Name = trimmedName,
            CreatedUtc = createdUtc,
            FolderId = resolvedFolderId,
            EntryIds = entries.Select(static e => e.Id).ToList(),
            Entries = entries,
        };

        int idx = _groups.FindIndex(g => g.Id == groupId);
        if (idx >= 0)
            _groups[idx] = group;
        else
            _groups.Insert(0, group);

        await WriteGroupsFileAsync(new Dictionary<string, List<ArchivedGroupEntryDto>>(StringComparer.Ordinal)
        {
            [groupId] = entries.Select(ToArchivedDto).ToList(),
        }).ConfigureAwait(false);

        LogDebug($"ApplyRemoteGroupAsync: applied synced group '{groupId}' ({entries.Count} entry(ies))");
    }
    finally
    {
        _gate.Release();
    }

    GroupsChanged?.Invoke(this, EventArgs.Empty);
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Expected: 4 new passed, plus the full existing `ClipboardGroupServiceTests` suite still green.

- [ ] **Step 6: Commit**

```bash
git add src/Clipt/Services/IClipboardGroupService.cs src/Clipt/Services/ClipboardGroupService.cs tests/Clipt.Tests/Services/ClipboardGroupServiceTests.cs
git commit -m "Clipt: ApplyRemoteGroupAsync — materialize a synced group locally"
```

---

### Task 6: HTTP API client

**Files:**
- Create: `src/Clipt/Services/Sync/IGroupSyncApiClient.cs`
- Create: `src/Clipt/Services/Sync/GroupSyncApiClient.cs`
- Test: `tests/Clipt.Tests/Services/Sync/GroupSyncApiClientTests.cs`

**Interfaces:**
- Produces: `IGroupSyncApiClient.{Configure, GetKdfAsync, PullGroupsAsync, PushGroupAsync, DeleteGroupAsync}`, `GroupSyncKdfParams`, `GroupSyncPullResult`, `GroupSyncPulledGroup`, `GroupSyncConflictException` — all consumed by Task 8.

- [ ] **Step 1: Write the failing tests**

`tests/Clipt.Tests/Services/Sync/GroupSyncApiClientTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Clipt.Services.Sync;

namespace Clipt.Tests.Services.Sync;

public class GroupSyncApiClientTests
{
    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }
        public required Func<HttpRequestMessage, HttpResponseMessage> Respond { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return Respond(request);
        }
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, object body) => new(status)
    {
        Content = JsonContent.Create(body),
    };

    [Fact]
    public void Configure_SetsBaseAddressAndAuthorizationHeader()
    {
        var handler = new StubHttpMessageHandler { Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) };
        var httpClient = new HttpClient(handler);
        var client = new GroupSyncApiClient(httpClient);

        client.Configure(new Uri("https://sync.example.com/"), "my-token");

        Assert.Equal(new Uri("https://sync.example.com/"), httpClient.BaseAddress);
        Assert.Equal("Bearer", httpClient.DefaultRequestHeaders.Authorization!.Scheme);
        Assert.Equal("my-token", httpClient.DefaultRequestHeaders.Authorization!.Parameter);
    }

    [Fact]
    public async Task GetKdfAsync_ParsesResponse()
    {
        string saltBase64 = Convert.ToBase64String([1, 2, 3, 4]);
        var handler = new StubHttpMessageHandler
        {
            Respond = _ => JsonResponse(HttpStatusCode.OK, new
            {
                salt = saltBase64,
                argon2_time_cost = 3,
                argon2_memory_cost_kib = 65536,
                argon2_parallelism = 4,
            }),
        };
        var client = new GroupSyncApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://sync.example.com/") });

        GroupSyncKdfParams result = await client.GetKdfAsync();

        Assert.Equal([1, 2, 3, 4], result.Salt);
        Assert.Equal(3, result.TimeCost);
        Assert.Equal(65536, result.MemoryCostKib);
        Assert.Equal(4, result.Parallelism);
    }

    [Fact]
    public async Task PullGroupsAsync_ParsesGroupsAndTombstones()
    {
        string ciphertextBase64 = Convert.ToBase64String([9, 9, 9]);
        var handler = new StubHttpMessageHandler
        {
            Respond = _ => JsonResponse(HttpStatusCode.OK, new
            {
                groups = new object[]
                {
                    new { id = "g1", ciphertext = ciphertextBase64, version = 5, updated_at = "2026-01-01T00:00:00Z", deleted = false },
                    new { id = "g2", ciphertext = (string?)null, version = 6, updated_at = "2026-01-01T00:00:00Z", deleted = true },
                },
                latest_version = 6,
            }),
        };
        var client = new GroupSyncApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://sync.example.com/") });

        GroupSyncPullResult result = await client.PullGroupsAsync(since: 0);

        Assert.Equal(6, result.LatestVersion);
        Assert.Equal(2, result.Groups.Count);
        Assert.Equal([9, 9, 9], result.Groups[0].Ciphertext);
        Assert.False(result.Groups[0].Deleted);
        Assert.Null(result.Groups[1].Ciphertext);
        Assert.True(result.Groups[1].Deleted);
    }

    [Fact]
    public async Task PushGroupAsync_Success_ReturnsNewVersion()
    {
        var handler = new StubHttpMessageHandler
        {
            Respond = _ => JsonResponse(HttpStatusCode.OK, new { id = "g1", version = 7 }),
        };
        var client = new GroupSyncApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://sync.example.com/") });

        int version = await client.PushGroupAsync("g1", [1, 2, 3], basedOnVersion: 6);

        Assert.Equal(7, version);
    }

    [Fact]
    public async Task PushGroupAsync_Conflict_ThrowsGroupSyncConflictException()
    {
        var handler = new StubHttpMessageHandler { Respond = _ => new HttpResponseMessage(HttpStatusCode.Conflict) };
        var client = new GroupSyncApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://sync.example.com/") });

        await Assert.ThrowsAsync<GroupSyncConflictException>(() => client.PushGroupAsync("g1", [1], basedOnVersion: 0));
    }

    [Fact]
    public async Task DeleteGroupAsync_Conflict_ThrowsGroupSyncConflictException()
    {
        var handler = new StubHttpMessageHandler { Respond = _ => new HttpResponseMessage(HttpStatusCode.Conflict) };
        var client = new GroupSyncApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://sync.example.com/") });

        await Assert.ThrowsAsync<GroupSyncConflictException>(() => client.DeleteGroupAsync("g1", basedOnVersion: 3));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Expected: build FAILS — `GroupSyncApiClient` doesn't exist.

- [ ] **Step 3: Implement `IGroupSyncApiClient.cs`**

```csharp
namespace Clipt.Services.Sync;

public interface IGroupSyncApiClient
{
    void Configure(Uri baseAddress, string bearerToken);

    Task<GroupSyncKdfParams> GetKdfAsync(CancellationToken cancellationToken = default);

    Task<GroupSyncPullResult> PullGroupsAsync(long since, CancellationToken cancellationToken = default);

    /// <summary>Throws <see cref="GroupSyncConflictException"/> on a 409 (based_on_version stale).</summary>
    Task<int> PushGroupAsync(string groupId, byte[] ciphertext, int basedOnVersion, CancellationToken cancellationToken = default);

    /// <summary>Throws <see cref="GroupSyncConflictException"/> on a 409 (based_on_version stale).</summary>
    Task<int> DeleteGroupAsync(string groupId, int basedOnVersion, CancellationToken cancellationToken = default);
}

public sealed record GroupSyncKdfParams(byte[] Salt, int TimeCost, int MemoryCostKib, int Parallelism);

public sealed record GroupSyncPullResult(IReadOnlyList<GroupSyncPulledGroup> Groups, long LatestVersion);

public sealed record GroupSyncPulledGroup(string Id, byte[]? Ciphertext, int Version, bool Deleted);

public sealed class GroupSyncConflictException() : Exception("The server has a newer version of this group.");
```

- [ ] **Step 4: Implement `GroupSyncApiClient.cs`**

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Clipt.Services.Sync;

public sealed class GroupSyncApiClient : IGroupSyncApiClient
{
    private readonly HttpClient _httpClient;

    public GroupSyncApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public void Configure(Uri baseAddress, string bearerToken)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        ArgumentException.ThrowIfNullOrEmpty(bearerToken);

        _httpClient.BaseAddress = baseAddress;
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
    }

    public async Task<GroupSyncKdfParams> GetKdfAsync(CancellationToken cancellationToken = default)
    {
        KdfResponseDto dto = await _httpClient.GetFromJsonAsync<KdfResponseDto>("kdf", cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Server returned an empty /kdf response.");

        return new GroupSyncKdfParams(
            Convert.FromBase64String(dto.Salt), dto.ArgonTimeCost, dto.ArgonMemoryCostKib, dto.ArgonParallelism);
    }

    public async Task<GroupSyncPullResult> PullGroupsAsync(long since, CancellationToken cancellationToken = default)
    {
        PullResponseDto dto = await _httpClient
            .GetFromJsonAsync<PullResponseDto>($"groups?since={since}", cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Server returned an empty /groups response.");

        List<GroupSyncPulledGroup> groups = dto.Groups
            .Select(g => new GroupSyncPulledGroup(
                g.Id, g.Ciphertext is null ? null : Convert.FromBase64String(g.Ciphertext), g.Version, g.Deleted))
            .ToList();

        return new GroupSyncPullResult(groups, dto.LatestVersion);
    }

    public async Task<int> PushGroupAsync(
        string groupId, byte[] ciphertext, int basedOnVersion, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(groupId);
        ArgumentNullException.ThrowIfNull(ciphertext);

        var body = new UpsertRequestDto(Convert.ToBase64String(ciphertext), basedOnVersion);
        HttpResponseMessage response = await _httpClient
            .PutAsJsonAsync($"groups/{Uri.EscapeDataString(groupId)}", body, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Conflict)
            throw new GroupSyncConflictException();
        response.EnsureSuccessStatusCode();

        UpsertResponseDto dto = await response.Content.ReadFromJsonAsync<UpsertResponseDto>(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Server returned an empty upsert response.");
        return dto.Version;
    }

    public async Task<int> DeleteGroupAsync(
        string groupId, int basedOnVersion, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(groupId);

        var request = new HttpRequestMessage(HttpMethod.Delete, $"groups/{Uri.EscapeDataString(groupId)}")
        {
            Content = JsonContent.Create(new DeleteRequestDto(basedOnVersion)),
        };
        HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Conflict)
            throw new GroupSyncConflictException();
        response.EnsureSuccessStatusCode();

        UpsertResponseDto dto = await response.Content.ReadFromJsonAsync<UpsertResponseDto>(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Server returned an empty delete response.");
        return dto.Version;
    }

    private sealed record KdfResponseDto(
        [property: JsonPropertyName("salt")] string Salt,
        [property: JsonPropertyName("argon2_time_cost")] int ArgonTimeCost,
        [property: JsonPropertyName("argon2_memory_cost_kib")] int ArgonMemoryCostKib,
        [property: JsonPropertyName("argon2_parallelism")] int ArgonParallelism);

    private sealed record PullResponseDto(
        [property: JsonPropertyName("groups")] List<GroupRecordDto> Groups,
        [property: JsonPropertyName("latest_version")] long LatestVersion);

    private sealed record GroupRecordDto(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("ciphertext")] string? Ciphertext,
        [property: JsonPropertyName("version")] int Version,
        [property: JsonPropertyName("deleted")] bool Deleted);

    private sealed record UpsertRequestDto(
        [property: JsonPropertyName("ciphertext")] string Ciphertext,
        [property: JsonPropertyName("based_on_version")] int BasedOnVersion);

    private sealed record DeleteRequestDto(
        [property: JsonPropertyName("based_on_version")] int BasedOnVersion);

    private sealed record UpsertResponseDto(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("version")] int Version);
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Expected: 6 passed.

- [ ] **Step 6: Commit**

```bash
git add src/Clipt/Services/Sync/IGroupSyncApiClient.cs src/Clipt/Services/Sync/GroupSyncApiClient.cs tests/Clipt.Tests/Services/Sync/GroupSyncApiClientTests.cs
git commit -m "Clipt: HTTP client for the group sync API"
```

---

### Task 7: Sync settings persistence

**Files:**
- Modify: `src/Clipt/Services/ISettingsService.cs`
- Modify: `src/Clipt/Services/SettingsService.cs`
- Test: `tests/Clipt.Tests/Services/SettingsServiceTests.cs`

**Interfaces:**
- Produces: `ISettingsService.{LoadGroupSyncServerUrl, SaveGroupSyncServerUrl, LoadGroupSyncToken, SaveGroupSyncToken, LoadGroupSyncEnabled, SaveGroupSyncEnabled}` — consumed by Task 8 (`GroupSyncService`).

- [ ] **Step 1: Write the failing tests**

Append to `tests/Clipt.Tests/Services/SettingsServiceTests.cs`:
```csharp
[Fact]
public void SaveAndLoadGroupSyncServerUrl_RoundTrips()
{
    var service = new SettingsService();

    service.SaveGroupSyncServerUrl("https://sync.monkeyskin.au/");
    Assert.Equal("https://sync.monkeyskin.au/", service.LoadGroupSyncServerUrl());

    service.SaveGroupSyncServerUrl(null);
    Assert.Null(service.LoadGroupSyncServerUrl());
}

[Fact]
public void SaveAndLoadGroupSyncToken_RoundTrips()
{
    var service = new SettingsService();

    service.SaveGroupSyncToken("secret-token");
    Assert.Equal("secret-token", service.LoadGroupSyncToken());

    service.SaveGroupSyncToken(null);
    Assert.Null(service.LoadGroupSyncToken());
}

[Fact]
public void LoadGroupSyncEnabled_DefaultsFalse()
{
    var service = new SettingsService();
    Assert.False(service.LoadGroupSyncEnabled());
}

[Fact]
public void SaveAndLoadGroupSyncEnabled_RoundTrips()
{
    var service = new SettingsService();

    service.SaveGroupSyncEnabled(true);
    Assert.True(service.LoadGroupSyncEnabled());

    service.SaveGroupSyncEnabled(false);
    Assert.False(service.LoadGroupSyncEnabled());
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Expected: build FAILS — the new members don't exist on `ISettingsService`/`SettingsService`.

- [ ] **Step 3: Add the members to `ISettingsService.cs`**

Add after `SaveTrayPopupSize`:
```csharp
string? LoadGroupSyncServerUrl();
void SaveGroupSyncServerUrl(string? url);

/// <summary>The sync API bearer token. Distinct from the encryption passphrase, which is never persisted.</summary>
string? LoadGroupSyncToken();
void SaveGroupSyncToken(string? token);

bool LoadGroupSyncEnabled();
void SaveGroupSyncEnabled(bool enabled);
```

- [ ] **Step 4: Implement them in `SettingsService.cs`**

Add these value-name constants alongside the existing ones:
```csharp
private const string GroupSyncServerUrlValueName = "GroupSyncServerUrl";
private const string GroupSyncTokenValueName = "GroupSyncToken";
private const string GroupSyncEnabledValueName = "GroupSyncEnabled";
```

Add the implementations (anywhere among the other `Load*`/`Save*` method pairs):
```csharp
public string? LoadGroupSyncServerUrl()
{
    try
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
        return key?.GetValue(GroupSyncServerUrlValueName) as string;
    }
    catch (System.Security.SecurityException)
    {
    }
    catch (IOException)
    {
    }

    return null;
}

public void SaveGroupSyncServerUrl(string? url)
{
    try
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
        if (string.IsNullOrEmpty(url))
            key.DeleteValue(GroupSyncServerUrlValueName, throwOnMissingValue: false);
        else
            key.SetValue(GroupSyncServerUrlValueName, url, RegistryValueKind.String);
    }
    catch (System.Security.SecurityException)
    {
    }
    catch (UnauthorizedAccessException)
    {
    }
}

public string? LoadGroupSyncToken()
{
    try
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
        return key?.GetValue(GroupSyncTokenValueName) as string;
    }
    catch (System.Security.SecurityException)
    {
    }
    catch (IOException)
    {
    }

    return null;
}

public void SaveGroupSyncToken(string? token)
{
    try
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
        if (string.IsNullOrEmpty(token))
            key.DeleteValue(GroupSyncTokenValueName, throwOnMissingValue: false);
        else
            key.SetValue(GroupSyncTokenValueName, token, RegistryValueKind.String);
    }
    catch (System.Security.SecurityException)
    {
    }
    catch (UnauthorizedAccessException)
    {
    }
}

public bool LoadGroupSyncEnabled()
{
    try
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
        if (key?.GetValue(GroupSyncEnabledValueName) is int value)
            return value != 0;
    }
    catch (System.Security.SecurityException)
    {
    }
    catch (IOException)
    {
    }

    return false;
}

public void SaveGroupSyncEnabled(bool enabled)
{
    try
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
        key.SetValue(GroupSyncEnabledValueName, enabled ? 1 : 0, RegistryValueKind.DWord);
    }
    catch (System.Security.SecurityException)
    {
    }
    catch (UnauthorizedAccessException)
    {
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Expected: 4 new passed, plus the full existing `SettingsServiceTests` suite still green.

- [ ] **Step 6: Commit**

```bash
git add src/Clipt/Services/ISettingsService.cs src/Clipt/Services/SettingsService.cs tests/Clipt.Tests/Services/SettingsServiceTests.cs
git commit -m "Clipt: sync server URL/token/enabled settings"
```

---

### Task 8: `GroupSyncService` — the orchestrator

**Files:**
- Create: `src/Clipt/Services/Sync/IGroupSyncService.cs`
- Create: `src/Clipt/Services/Sync/GroupSyncService.cs`
- Test: `tests/Clipt.Tests/Services/Sync/GroupSyncServiceTests.cs`

**Interfaces:**
- Consumes: `IClipboardGroupService` (existing + Task 5's `ApplyRemoteGroupAsync`), `IGroupSyncApiClient` (Task 6), `IGroupSyncCrypto` (Task 1), `IGroupSyncStateStore` (Task 3), `IGroupEntryBlobReader` (Task 4), `ISettingsService` (Task 7), `IAppLogger?` (existing).
- Produces: `IGroupSyncService.{IsConfigured, IsUnlocked, LastSuccessfulSyncUtc, LastError, EnableAsync, UnlockAsync, Disable, SyncNowAsync, StatusChanged}` — consumed by Task 9 (DI wiring, UI dialog).

This is the biggest task in this plan. Tests drive the push/pull/conflict contract directly through `SyncNowAsync` rather than through the internal debounce timer (which is exercised for real only by manual/App testing in Task 9 — timers are deliberately not unit-tested here, since asserting on wall-clock timer firing is exactly the kind of flaky test this plan should avoid).

- [ ] **Step 1: Write the failing tests**

`tests/Clipt.Tests/Services/Sync/GroupSyncServiceTests.cs`:
```csharp
using Clipt.Models;
using Clipt.Services;
using Clipt.Services.Sync;
using Moq;

namespace Clipt.Tests.Services.Sync;

public class GroupSyncServiceTests
{
    private readonly Mock<IClipboardGroupService> _groupServiceMock = new();
    private readonly Mock<IGroupSyncApiClient> _apiClientMock = new();
    private readonly Mock<IGroupSyncCrypto> _cryptoMock = new();
    private readonly Mock<IGroupSyncStateStore> _stateStoreMock = new();
    private readonly Mock<IGroupEntryBlobReader> _blobReaderMock = new();
    private readonly Mock<ISettingsService> _settingsMock = new();

    private static readonly byte[] FakeKey = new byte[32];

    public GroupSyncServiceTests()
    {
        _settingsMock.Setup(s => s.LoadGroupSyncServerUrl()).Returns((string?)null);
        _settingsMock.Setup(s => s.LoadGroupSyncToken()).Returns((string?)null);
        _settingsMock.Setup(s => s.LoadGroupSyncEnabled()).Returns(false);
        _groupServiceMock.Setup(g => g.Groups).Returns([]);
        _apiClientMock
            .Setup(a => a.GetKdfAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GroupSyncKdfParams([1, 2, 3, 4], 3, 65536, 4));
        _cryptoMock
            .Setup(c => c.DeriveKey(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(FakeKey);
        _stateStoreMock.Setup(s => s.LoadAsync()).ReturnsAsync(new GroupSyncState());
        _apiClientMock
            .Setup(a => a.PullGroupsAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GroupSyncPullResult([], 0));
    }

    private GroupSyncService CreateService() => new(
        _groupServiceMock.Object,
        _apiClientMock.Object,
        _cryptoMock.Object,
        _stateStoreMock.Object,
        _blobReaderMock.Object,
        _settingsMock.Object);

    private static ClipboardGroup MakeGroup(string id, params string[] entryIds) => new()
    {
        Id = id,
        Name = "Group",
        CreatedUtc = DateTime.UtcNow,
        EntryIds = entryIds.ToList(),
        Entries = entryIds.Select(eid => new ArchivedGroupEntryInfo(
            Id: eid, SourceEntryId: "", Name: "Clip", TimestampUtc: DateTime.UtcNow,
            SequenceNumber: 0, OwnerProcess: "test", OwnerPid: 0, Summary: "Clip",
            ContentType: ContentType.Text, DataSizeBytes: 1, ContentHash: "x")).ToList(),
    };

    [Fact]
    public async Task EnableAsync_ConfiguresApiClientDerivesKeyAndSavesSettings()
    {
        var service = CreateService();

        await service.EnableAsync(new Uri("https://sync.example.com/"), "token", "passphrase");

        _apiClientMock.Verify(a => a.Configure(new Uri("https://sync.example.com/"), "token"), Times.Once);
        _settingsMock.Verify(s => s.SaveGroupSyncServerUrl("https://sync.example.com/"), Times.Once);
        _settingsMock.Verify(s => s.SaveGroupSyncToken("token"), Times.Once);
        _settingsMock.Verify(s => s.SaveGroupSyncEnabled(true), Times.Once);
        Assert.True(service.IsConfigured);
        Assert.True(service.IsUnlocked);
    }

    [Fact]
    public async Task EnableAsync_PushesExistingLocalGroupWithBasedOnVersionZero()
    {
        _groupServiceMock.Setup(g => g.Groups).Returns([MakeGroup("g1", "e1")]);
        _blobReaderMock.Setup(b => b.ReadEntryBlobAsync("g1", "e1")).ReturnsAsync([1, 2, 3]);
        _cryptoMock.Setup(c => c.Encrypt(FakeKey, It.IsAny<byte[]>())).Returns([9, 9, 9]);
        _apiClientMock
            .Setup(a => a.PushGroupAsync("g1", It.IsAny<byte[]>(), 0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var service = CreateService();
        await service.EnableAsync(new Uri("https://sync.example.com/"), "token", "passphrase");

        _apiClientMock.Verify(a => a.PushGroupAsync("g1", It.IsAny<byte[]>(), 0, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncNowAsync_NotUnlocked_DoesNothing()
    {
        var service = CreateService(); // EnableAsync never called

        await service.SyncNowAsync();

        _apiClientMock.Verify(a => a.PullGroupsAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncNowAsync_UnchangedGroup_SkipsPush()
    {
        var group = MakeGroup("g1", "e1");
        _groupServiceMock.Setup(g => g.Groups).Returns([group]);
        _blobReaderMock.Setup(b => b.ReadEntryBlobAsync("g1", "e1")).ReturnsAsync([1, 2, 3]);

        GroupSyncEnvelope envelope = GroupSyncEnvelopeConverter.ToEnvelope(group, new Dictionary<string, byte[]> { ["e1"] = [1, 2, 3] });
        string hash = GroupSyncEnvelopeConverter.ComputeContentHash(envelope);
        var state = new GroupSyncState();
        state.Groups["g1"] = new GroupSyncStateEntry { SyncedVersion = 1, ContentHash = hash };
        _stateStoreMock.Setup(s => s.LoadAsync()).ReturnsAsync(state);

        var service = CreateService();
        await service.EnableAsync(new Uri("https://sync.example.com/"), "token", "passphrase");
        _apiClientMock.Invocations.Clear();

        await service.SyncNowAsync();

        _apiClientMock.Verify(a => a.PushGroupAsync(
            It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SyncNowAsync_LocallyDeletedGroup_CallsDeleteGroupAsync()
    {
        var state = new GroupSyncState();
        state.Groups["gone"] = new GroupSyncStateEntry { SyncedVersion = 4, ContentHash = "irrelevant" };
        _stateStoreMock.Setup(s => s.LoadAsync()).ReturnsAsync(state);
        _groupServiceMock.Setup(g => g.Groups).Returns([]); // "gone" no longer exists locally
        _apiClientMock
            .Setup(a => a.DeleteGroupAsync("gone", 4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);

        var service = CreateService();
        await service.EnableAsync(new Uri("https://sync.example.com/"), "token", "passphrase");

        _apiClientMock.Verify(a => a.DeleteGroupAsync("gone", 4, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SyncNowAsync_PullAppliesRemoteGroup_WhenNotLocallyDirty()
    {
        GroupSyncEnvelope envelope = new()
        {
            Name = "Remote Group",
            FolderId = null,
            CreatedUtc = DateTime.UtcNow,
            Entries = [new GroupSyncEnvelopeEntry { Id = "e1", Name = "Clip", TimestampUtc = DateTime.UtcNow, ContentType = ContentType.Text, BlobBase64 = Convert.ToBase64String([1, 2, 3]) }],
        };
        byte[] plaintext = GroupSyncEnvelopeConverter.SerializeToBytes(envelope);
        _apiClientMock
            .Setup(a => a.PullGroupsAsync(0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GroupSyncPullResult([new GroupSyncPulledGroup("remote1", [9, 9, 9], 3, false)], 3));
        _cryptoMock.Setup(c => c.Decrypt(FakeKey, new byte[] { 9, 9, 9 })).Returns(plaintext);

        var service = CreateService();
        await service.EnableAsync(new Uri("https://sync.example.com/"), "token", "passphrase");

        _groupServiceMock.Verify(g => g.ApplyRemoteGroupAsync(
            "remote1", "Remote Group", null, envelope.CreatedUtc,
            It.Is<IReadOnlyList<ArchivedGroupEntryInfo>>(e => e.Count == 1 && e[0].Id == "e1"),
            It.Is<IReadOnlyDictionary<string, byte[]>>(b => b["e1"].SequenceEqual(new byte[] { 1, 2, 3 }))),
            Times.Once);
    }

    [Fact]
    public async Task SyncNowAsync_PullTombstone_DeletesLocalGroupIfPresent()
    {
        _groupServiceMock.Setup(g => g.Groups).Returns([MakeGroup("g1", "e1")]);
        var state = new GroupSyncState();
        state.Groups["g1"] = new GroupSyncStateEntry { SyncedVersion = 2, ContentHash = "will-not-match-anyway" };
        _stateStoreMock.Setup(s => s.LoadAsync()).ReturnsAsync(state);
        _blobReaderMock.Setup(b => b.ReadEntryBlobAsync("g1", "e1")).ReturnsAsync([1, 2, 3]);
        // Make the local content hash match tracked state, so the incoming tombstone isn't skipped as "locally dirty".
        GroupSyncEnvelope localEnvelope = GroupSyncEnvelopeConverter.ToEnvelope(MakeGroup("g1", "e1"), new Dictionary<string, byte[]> { ["e1"] = [1, 2, 3] });
        state.Groups["g1"].ContentHash = GroupSyncEnvelopeConverter.ComputeContentHash(localEnvelope);
        _apiClientMock
            .Setup(a => a.PullGroupsAsync(0, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GroupSyncPullResult([new GroupSyncPulledGroup("g1", null, 3, true)], 3));

        var service = CreateService();
        await service.EnableAsync(new Uri("https://sync.example.com/"), "token", "passphrase");

        _groupServiceMock.Verify(g => g.DeleteGroupAsync("g1"), Times.Once);
    }

    [Fact]
    public async Task PushConflict_PullsThenRetriesOnce_ThenSucceeds()
    {
        var group = MakeGroup("g1", "e1");
        _groupServiceMock.Setup(g => g.Groups).Returns([group]);
        _blobReaderMock.Setup(b => b.ReadEntryBlobAsync("g1", "e1")).ReturnsAsync([1, 2, 3]);
        var callCount = 0;
        _apiClientMock
            .Setup(a => a.PushGroupAsync("g1", It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                return callCount == 1 ? throw new GroupSyncConflictException() : Task.FromResult(9);
            });

        var service = CreateService();
        await service.EnableAsync(new Uri("https://sync.example.com/"), "token", "passphrase");

        Assert.Equal(2, callCount);
        _apiClientMock.Verify(a => a.PullGroupsAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.AtLeast(2));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Expected: build FAILS — `GroupSyncService`/`IGroupSyncService` don't exist.

- [ ] **Step 3: Implement `IGroupSyncService.cs`**

```csharp
namespace Clipt.Services.Sync;

public interface IGroupSyncService
{
    /// <summary>True once a server URL + token have been saved (persists across app restarts).</summary>
    bool IsConfigured { get; }

    /// <summary>True once the passphrase has been entered this session and the key derived. Required before any sync can run.</summary>
    bool IsUnlocked { get; }

    DateTime? LastSuccessfulSyncUtc { get; }
    string? LastError { get; }

    /// <summary>First-time setup: saves the server URL + token, derives the key from the passphrase, and runs an initial sync.</summary>
    Task EnableAsync(Uri serverUrl, string token, string passphrase, CancellationToken cancellationToken = default);

    /// <summary>Re-derives the key from the passphrase using the already-saved server URL + token (e.g. after an app restart).</summary>
    Task UnlockAsync(string passphrase, CancellationToken cancellationToken = default);

    /// <summary>Clears the saved server URL/token/enabled flag and the in-memory key. Sync stops until EnableAsync is called again.</summary>
    void Disable();

    /// <summary>Pushes any locally dirty groups, then pulls remote changes. No-op if not unlocked.</summary>
    Task SyncNowAsync(CancellationToken cancellationToken = default);

    event EventHandler? StatusChanged;
}
```

- [ ] **Step 4: Implement `GroupSyncService.cs`**

```csharp
namespace Clipt.Services.Sync;

using Clipt.Models;

public sealed class GroupSyncService : IGroupSyncService, IDisposable
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PullInterval = TimeSpan.FromSeconds(60);

    private readonly IClipboardGroupService _groupService;
    private readonly IGroupSyncApiClient _apiClient;
    private readonly IGroupSyncCrypto _crypto;
    private readonly IGroupSyncStateStore _stateStore;
    private readonly IGroupEntryBlobReader _blobReader;
    private readonly ISettingsService _settingsService;
    private readonly IAppLogger? _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private GroupSyncState _state = new();
    private byte[]? _key;
    private Timer? _debounceTimer;
    private Timer? _pullTimer;
    private bool _subscribedToGroupsChanged;

    public GroupSyncService(
        IClipboardGroupService groupService,
        IGroupSyncApiClient apiClient,
        IGroupSyncCrypto crypto,
        IGroupSyncStateStore stateStore,
        IGroupEntryBlobReader blobReader,
        ISettingsService settingsService,
        IAppLogger? logger = null)
    {
        _groupService = groupService ?? throw new ArgumentNullException(nameof(groupService));
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _crypto = crypto ?? throw new ArgumentNullException(nameof(crypto));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _blobReader = blobReader ?? throw new ArgumentNullException(nameof(blobReader));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _logger = logger;

        IsConfigured = !string.IsNullOrEmpty(_settingsService.LoadGroupSyncServerUrl())
            && !string.IsNullOrEmpty(_settingsService.LoadGroupSyncToken())
            && _settingsService.LoadGroupSyncEnabled();
    }

    public bool IsConfigured { get; private set; }
    public bool IsUnlocked => _key is not null;
    public DateTime? LastSuccessfulSyncUtc { get; private set; }
    public string? LastError { get; private set; }

    public event EventHandler? StatusChanged;

    public async Task EnableAsync(Uri serverUrl, string token, string passphrase, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serverUrl);
        ArgumentException.ThrowIfNullOrEmpty(token);
        ArgumentException.ThrowIfNullOrEmpty(passphrase);

        await DeriveAndCacheKeyAsync(serverUrl, token, passphrase, cancellationToken).ConfigureAwait(false);

        _settingsService.SaveGroupSyncServerUrl(serverUrl.ToString());
        _settingsService.SaveGroupSyncToken(token);
        _settingsService.SaveGroupSyncEnabled(true);
        IsConfigured = true;

        _state = await _stateStore.LoadAsync().ConfigureAwait(false);
        EnsureSubscribedAndTimersRunning();

        await SyncNowAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UnlockAsync(string passphrase, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(passphrase);

        string? url = _settingsService.LoadGroupSyncServerUrl();
        string? token = _settingsService.LoadGroupSyncToken();
        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(token))
            throw new InvalidOperationException("Sync has not been set up yet — call EnableAsync first.");

        await DeriveAndCacheKeyAsync(new Uri(url), token, passphrase, cancellationToken).ConfigureAwait(false);

        _state = await _stateStore.LoadAsync().ConfigureAwait(false);
        EnsureSubscribedAndTimersRunning();
    }

    public void Disable()
    {
        _settingsService.SaveGroupSyncEnabled(false);
        IsConfigured = false;
        _key = null;

        if (_subscribedToGroupsChanged)
        {
            _groupService.GroupsChanged -= OnGroupsChanged;
            _subscribedToGroupsChanged = false;
        }

        _debounceTimer?.Dispose();
        _debounceTimer = null;
        _pullTimer?.Dispose();
        _pullTimer = null;
    }

    public async Task SyncNowAsync(CancellationToken cancellationToken = default)
    {
        if (!IsUnlocked)
            return;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await PushDirtyGroupsAsync(cancellationToken).ConfigureAwait(false);
            await PullRemoteChangesAsync(cancellationToken).ConfigureAwait(false);
            LastSuccessfulSyncUtc = DateTime.UtcNow;
            LastError = null;
        }
        finally
        {
            _gate.Release();
        }

        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task DeriveAndCacheKeyAsync(Uri serverUrl, string token, string passphrase, CancellationToken cancellationToken)
    {
        _apiClient.Configure(serverUrl, token);
        GroupSyncKdfParams kdf = await _apiClient.GetKdfAsync(cancellationToken).ConfigureAwait(false);
        _key = _crypto.DeriveKey(passphrase, kdf.Salt, kdf.TimeCost, kdf.MemoryCostKib, kdf.Parallelism);
    }

    private void EnsureSubscribedAndTimersRunning()
    {
        if (!_subscribedToGroupsChanged)
        {
            _groupService.GroupsChanged += OnGroupsChanged;
            _subscribedToGroupsChanged = true;
        }

        _pullTimer ??= new Timer(_ => RunBackgroundSync(), null, PullInterval, PullInterval);
    }

    private void OnGroupsChanged(object? sender, EventArgs e)
    {
        _debounceTimer?.Dispose();
        _debounceTimer = new Timer(_ => RunBackgroundSync(), null, DebounceDelay, Timeout.InfiniteTimeSpan);
    }

    private void RunBackgroundSync() => _ = RunBackgroundSyncAsync();

    private async Task RunBackgroundSyncAsync()
    {
        try
        {
            await SyncNowAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or GroupSyncConflictException)
        {
            LastError = ex.Message;
            _logger?.Warn($"[GroupSync] background sync failed — {ex.Message}");
        }
    }

    private async Task PushDirtyGroupsAsync(CancellationToken cancellationToken)
    {
        foreach (ClipboardGroup group in _groupService.Groups.ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var blobs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (ArchivedGroupEntryInfo entry in group.Entries)
                blobs[entry.Id] = await _blobReader.ReadEntryBlobAsync(group.Id, entry.Id).ConfigureAwait(false);

            GroupSyncEnvelope envelope = GroupSyncEnvelopeConverter.ToEnvelope(group, blobs);
            string hash = GroupSyncEnvelopeConverter.ComputeContentHash(envelope);

            if (_state.Groups.TryGetValue(group.Id, out GroupSyncStateEntry? existing) && existing.ContentHash == hash)
                continue;

            int basedOn = existing?.SyncedVersion ?? 0;
            byte[] ciphertext = _crypto.Encrypt(_key!, GroupSyncEnvelopeConverter.SerializeToBytes(envelope));

            int newVersion;
            try
            {
                newVersion = await _apiClient.PushGroupAsync(group.Id, ciphertext, basedOn, cancellationToken).ConfigureAwait(false);
            }
            catch (GroupSyncConflictException)
            {
                await PullRemoteChangesAsync(cancellationToken).ConfigureAwait(false);
                basedOn = _state.Groups.TryGetValue(group.Id, out GroupSyncStateEntry? refreshed) ? refreshed.SyncedVersion : 0;

                try
                {
                    newVersion = await _apiClient.PushGroupAsync(group.Id, ciphertext, basedOn, cancellationToken).ConfigureAwait(false);
                }
                catch (GroupSyncConflictException)
                {
                    _logger?.Warn($"[GroupSync] push conflict persisted for group '{group.Id}' after one retry — will try again next cycle");
                    continue;
                }
            }

            _state.Groups[group.Id] = new GroupSyncStateEntry { SyncedVersion = newVersion, ContentHash = hash };
        }

        var localIds = new HashSet<string>(_groupService.Groups.Select(static g => g.Id), StringComparer.Ordinal);
        foreach (string staleId in _state.Groups.Keys.Where(id => !localIds.Contains(id)).ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();
            int basedOn = _state.Groups[staleId].SyncedVersion;

            try
            {
                await _apiClient.DeleteGroupAsync(staleId, basedOn, cancellationToken).ConfigureAwait(false);
            }
            catch (GroupSyncConflictException)
            {
                _logger?.Warn($"[GroupSync] delete conflict for group '{staleId}' — will try again next cycle");
                continue;
            }

            _state.Groups.Remove(staleId);
        }

        await _stateStore.SaveAsync(_state).ConfigureAwait(false);
    }

    private async Task PullRemoteChangesAsync(CancellationToken cancellationToken)
    {
        GroupSyncPullResult result = await _apiClient.PullGroupsAsync(_state.GlobalLastPulledVersion, cancellationToken).ConfigureAwait(false);

        foreach (GroupSyncPulledGroup pulled in result.Groups)
        {
            if (await IsLocallyDirtyAsync(pulled.Id).ConfigureAwait(false))
                continue;

            if (pulled.Deleted)
            {
                if (_groupService.Groups.Any(g => g.Id == pulled.Id))
                    await _groupService.DeleteGroupAsync(pulled.Id).ConfigureAwait(false);

                _state.Groups.Remove(pulled.Id);
                continue;
            }

            byte[] plaintext = _crypto.Decrypt(_key!, pulled.Ciphertext!);
            GroupSyncEnvelope envelope = GroupSyncEnvelopeConverter.DeserializeFromBytes(plaintext);
            (List<ArchivedGroupEntryInfo> entries, Dictionary<string, byte[]> blobs) = GroupSyncEnvelopeConverter.FromEnvelope(envelope);

            await _groupService.ApplyRemoteGroupAsync(
                pulled.Id, envelope.Name, envelope.FolderId, envelope.CreatedUtc, entries, blobs).ConfigureAwait(false);

            _state.Groups[pulled.Id] = new GroupSyncStateEntry
            {
                SyncedVersion = pulled.Version,
                ContentHash = GroupSyncEnvelopeConverter.ComputeContentHash(envelope),
            };
        }

        _state.GlobalLastPulledVersion = Math.Max(_state.GlobalLastPulledVersion, result.LatestVersion);
        await _stateStore.SaveAsync(_state).ConfigureAwait(false);
    }

    private async Task<bool> IsLocallyDirtyAsync(string groupId)
    {
        ClipboardGroup? local = _groupService.Groups.FirstOrDefault(g => g.Id == groupId);
        if (local is null)
            return false;

        if (!_state.Groups.TryGetValue(groupId, out GroupSyncStateEntry? tracked))
            return true;

        var blobs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (ArchivedGroupEntryInfo entry in local.Entries)
            blobs[entry.Id] = await _blobReader.ReadEntryBlobAsync(local.Id, entry.Id).ConfigureAwait(false);

        string currentHash = GroupSyncEnvelopeConverter.ComputeContentHash(GroupSyncEnvelopeConverter.ToEnvelope(local, blobs));
        return currentHash != tracked.ContentHash;
    }

    public void Dispose()
    {
        _debounceTimer?.Dispose();
        _pullTimer?.Dispose();
        _gate.Dispose();
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Expected: 9 passed.

- [ ] **Step 6: Commit**

```bash
git add src/Clipt/Services/Sync/IGroupSyncService.cs src/Clipt/Services/Sync/GroupSyncService.cs tests/Clipt.Tests/Services/Sync/GroupSyncServiceTests.cs
git commit -m "Clipt: GroupSyncService — debounced push, periodic pull, conflict retry"
```

---

### Task 9: Setup dialog and tray menu wiring

**Files:**
- Create: `src/Clipt/Services/Sync/GroupSyncSetupWindow.xaml`
- Create: `src/Clipt/Services/Sync/GroupSyncSetupWindow.xaml.cs`
- Modify: `src/Clipt/Views/TrayPopupWindow.xaml`
- Modify: `src/Clipt/Views/TrayPopupWindow.xaml.cs`

No unit tests in this task — it's WPF Views/code-behind, and this codebase's existing convention (confirmed by `tests/Clipt.Tests/ViewModels/*` containing all the test coverage, with nothing under a `Views` test folder) is that Views aren't unit tested; logic lives in ViewModels/services, which Tasks 1–8 already cover. Verify this task by running the app (see Task 10's manual verification step, which covers both tasks together).

- [ ] **Step 1: Create the setup dialog XAML**

`src/Clipt/Services/Sync/GroupSyncSetupWindow.xaml`:
```xml
<Window x:Class="Clipt.Services.Sync.GroupSyncSetupWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Clipt — Group Sync"
        Width="380" SizeToContent="Height"
        WindowStartupLocation="CenterOwner"
        ResizeMode="NoResize"
        Background="{DynamicResource BackgroundBrush}">
    <StackPanel Margin="16">
        <TextBlock Text="Server URL" FontSize="11" Margin="0,0,0,2" Foreground="{DynamicResource ForegroundDimBrush}" />
        <TextBox x:Name="ServerUrlBox" Margin="0,0,0,10" />

        <TextBlock Text="Bearer token" FontSize="11" Margin="0,0,0,2" Foreground="{DynamicResource ForegroundDimBrush}" />
        <PasswordBox x:Name="TokenBox" Margin="0,0,0,10" />

        <TextBlock Text="Passphrase" FontSize="11" Margin="0,0,0,2" Foreground="{DynamicResource ForegroundDimBrush}" />
        <PasswordBox x:Name="PassphraseBox" Margin="0,0,0,10" />

        <TextBlock x:Name="StatusText" FontSize="11" TextWrapping="Wrap" Margin="0,0,0,10"
                   Foreground="{DynamicResource AccentRoseBrush}" Visibility="Collapsed" />

        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
            <Button x:Name="DisableButton" Content="Disable sync" Padding="10,4" Margin="0,0,8,0" Click="DisableButton_Click" />
            <Button x:Name="EnableButton" Content="Enable" Padding="10,4" Click="EnableButton_Click" IsDefault="True" />
        </StackPanel>
    </StackPanel>
</Window>
```

- [ ] **Step 2: Implement the code-behind**

`src/Clipt/Services/Sync/GroupSyncSetupWindow.xaml.cs`:
```csharp
using System.Windows;

namespace Clipt.Services.Sync;

public partial class GroupSyncSetupWindow : Window
{
    private readonly IGroupSyncService _syncService;

    public GroupSyncSetupWindow(IGroupSyncService syncService)
    {
        _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
        InitializeComponent();

        string? savedUrl = null;
        if (syncService.IsConfigured)
        {
            DisableButton.Visibility = Visibility.Visible;
            EnableButton.Content = "Update";
        }
        else
        {
            DisableButton.Visibility = Visibility.Collapsed;
        }

        _ = savedUrl; // server URL/token are write-only from this dialog's perspective — never re-displayed once saved.
    }

    private async void EnableButton_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Visibility = Visibility.Collapsed;
        EnableButton.IsEnabled = false;

        try
        {
            if (string.IsNullOrWhiteSpace(ServerUrlBox.Text) || !Uri.TryCreate(ServerUrlBox.Text.Trim(), UriKind.Absolute, out Uri? serverUrl))
            {
                ShowError("Enter a valid server URL, e.g. https://sync.monkeyskin.au/");
                return;
            }

            string token = TokenBox.Password;
            string passphrase = PassphraseBox.Password;
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(passphrase))
            {
                ShowError("Both the token and the passphrase are required.");
                return;
            }

            await _syncService.EnableAsync(serverUrl, token, passphrase);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError($"Could not enable sync: {ex.Message}");
        }
        finally
        {
            EnableButton.IsEnabled = true;
        }
    }

    private void DisableButton_Click(object sender, RoutedEventArgs e)
    {
        _syncService.Disable();
        DialogResult = true;
        Close();
    }

    private void ShowError(string message)
    {
        StatusText.Text = message;
        StatusText.Visibility = Visibility.Visible;
    }
}
```

- [ ] **Step 3: Add the "Sync…" menu item**

In `src/Clipt/Views/TrayPopupWindow.xaml`, inside the `TrayOptionsMenu`'s root `MenuItem` (the one with `Header="&#x2261;"`), add a new item after the existing `<Separator />` that follows `"Long text (More/Less)"` and before the `"Clear"` submenu:
```xml
<MenuItem Header="Sync&#x2026;"
          Click="SyncMenuItem_Click"
          ToolTip="Set up or manage end-to-end encrypted group sync" />
<Separator />
```

- [ ] **Step 4: Add the click handler**

In `src/Clipt/Views/TrayPopupWindow.xaml.cs`, add a field and a handler (near the other `*_Click` handlers such as `MoveToFolderButton_Click`):
```csharp
private readonly IGroupSyncService? _groupSyncService;
```

Thread this through the constructor (see Task 10 for the exact DI change) and add:
```csharp
private void SyncMenuItem_Click(object sender, RoutedEventArgs e)
{
    if (_groupSyncService is null)
        return;

    var dialog = new Clipt.Services.Sync.GroupSyncSetupWindow(_groupSyncService) { Owner = this };
    dialog.ShowDialog();
}
```

- [ ] **Step 5: Commit**

```bash
git add src/Clipt/Services/Sync/GroupSyncSetupWindow.xaml src/Clipt/Services/Sync/GroupSyncSetupWindow.xaml.cs src/Clipt/Views/TrayPopupWindow.xaml src/Clipt/Views/TrayPopupWindow.xaml.cs
git commit -m "Clipt: group sync setup dialog and tray menu entry"
```

---

### Task 10: DI wiring, startup unlock prompt, and pull-on-open

**Files:**
- Modify: `src/Clipt/App.xaml.cs`
- Modify: `src/Clipt/Views/TrayPopupWindow.xaml.cs` (constructor + `ShowNearTray`/activation hook — extends Task 9's changes)

No new unit tests — this task is composition-root wiring and a startup UX flow, verified manually (this is exactly the kind of change the project's own conventions say to verify by running the app, not by unit test).

- [ ] **Step 1: Register the new services in `App.xaml.cs`**

Find the `services.AddSingleton<IClipboardGroupService, ClipboardGroupService>();` line (around line 674 per this plan's research) and add after it:
```csharp
services.AddSingleton<HttpClient>();
services.AddSingleton<Clipt.Services.Sync.IGroupSyncCrypto, Clipt.Services.Sync.GroupSyncCrypto>();
services.AddSingleton<Clipt.Services.Sync.IGroupSyncStateStore, Clipt.Services.Sync.GroupSyncStateStore>();
services.AddSingleton<Clipt.Services.Sync.IGroupEntryBlobReader, Clipt.Services.Sync.GroupEntryBlobReader>();
services.AddSingleton<Clipt.Services.Sync.IGroupSyncApiClient, Clipt.Services.Sync.GroupSyncApiClient>();
services.AddSingleton<Clipt.Services.Sync.IGroupSyncService>(sp => new Clipt.Services.Sync.GroupSyncService(
    sp.GetRequiredService<IClipboardGroupService>(),
    sp.GetRequiredService<Clipt.Services.Sync.IGroupSyncApiClient>(),
    sp.GetRequiredService<Clipt.Services.Sync.IGroupSyncCrypto>(),
    sp.GetRequiredService<Clipt.Services.Sync.IGroupSyncStateStore>(),
    sp.GetRequiredService<Clipt.Services.Sync.IGroupEntryBlobReader>(),
    sp.GetRequiredService<ISettingsService>(),
    sp.GetRequiredService<IAppLogger>()));
```

- [ ] **Step 2: Update `TrayPopupWindow`'s DI registration to pass the new service**

Find `services.AddSingleton<TrayPopupWindow>();` and change it to construct explicitly (matching how `TrayPopupViewModel` is already constructed a few lines above it):
```csharp
services.AddSingleton<TrayPopupWindow>(sp => new TrayPopupWindow(
    sp.GetRequiredService<TrayPopupViewModel>(),
    sp.GetRequiredService<ISettingsService>(),
    sp.GetRequiredService<Clipt.Services.Sync.IGroupSyncService>()));
```

Update `TrayPopupWindow`'s constructor in `src/Clipt/Views/TrayPopupWindow.xaml.cs` to accept and store the new parameter:
```csharp
public TrayPopupWindow(
    TrayPopupViewModel viewModel,
    ISettingsService? settingsService = null,
    Clipt.Services.Sync.IGroupSyncService? groupSyncService = null)
{
    _settingsService = settingsService;
    _groupSyncService = groupSyncService;
    InitializeComponent();
    // ... existing body unchanged ...
```

- [ ] **Step 3: Trigger a sync on tray popup open**

In `src/Clipt/Views/TrayPopupWindow.xaml.cs`, find `ShowNearTray()` (added by the Spoon-aesthetic UI pass) and add a fire-and-forget sync trigger, matching the "never block the UI" rule from the design spec:
```csharp
public void ShowNearTray()
{
    var workArea = SystemParameters.WorkArea;
    Left = Math.Max(workArea.Left + 8, workArea.Right - Width - 8);
    Top = Math.Max(workArea.Top + 8, workArea.Bottom - Height - 8);
    Show();
    Activate();

    if (_groupSyncService?.IsUnlocked == true)
        _ = _groupSyncService.SyncNowAsync();
}
```

- [ ] **Step 4: Prompt for the passphrase once at startup, if sync was previously configured**

In `App.xaml.cs`, find where the tray window is first shown at startup (the existing startup sequence that resolves and shows `TrayPopupWindow`/`MainWindow`). After that point, add:
```csharp
var groupSyncService = _serviceProvider!.GetRequiredService<Clipt.Services.Sync.IGroupSyncService>();
if (groupSyncService.IsConfigured && !groupSyncService.IsUnlocked)
{
    var unlockDialog = new Clipt.Services.Sync.GroupSyncSetupWindow(groupSyncService);
    unlockDialog.ShowDialog();
}
```

This reuses the same setup dialog — since `IsConfigured` is already true, `EnableButton_Click`'s `EnableAsync` call will re-save the same URL/token (harmless) and simply re-derive the key from the freshly typed passphrase. A dedicated "just ask for the passphrase" dialog would be a reasonable follow-up if this dual-purpose dialog proves awkward in practice, but isn't needed to make the feature work correctly.

- [ ] **Step 5: Full build and test run**

Build both `src/Clipt/Clipt.csproj` and `tests/Clipt.Tests/Clipt.Tests.csproj`, then run the full test suite via `vstest.console.exe` (per this project's WSL→Windows-interop build recipe).
Expected: clean build, all tests passing (existing suite + every test added across Tasks 1–8).

- [ ] **Step 6: Manual verification**

Run the app (per this project's `run` conventions). Open the tray popup, use the options menu → "Sync…", enter a server URL/token/passphrase against a locally-running instance of the server from the companion server plan (`python -m uvicorn app.main:app --port 8100` with `CLIPT_SYNC_TOKEN` set, from `server/clipt-sync-server/`), click Enable, and confirm:
- The dialog closes without error.
- A second Clipt instance (or a second `%LocalAppData%\Clipt` profile) pointed at the same local server, with the same passphrase, receives an existing group within ~60 seconds (or immediately via the tray-popup-open pull).
- Renaming/moving/deleting a group entry on one side is reflected on the other within one debounce+pull cycle.

- [ ] **Step 7: Commit**

```bash
git add src/Clipt/App.xaml.cs src/Clipt/Views/TrayPopupWindow.xaml.cs
git commit -m "Clipt: wire up GroupSyncService — DI, startup unlock prompt, pull-on-open"
```

---

## Self-Review Notes

- **Spec coverage:** encryption/KDF (Task 1), envelope shape (Task 2), local sync bookkeeping (Task 3), blob access (Task 4), the one required Groups-storage extension (Task 5), the full HTTP contract from the server plan (Task 6), persisted server URL/token with the passphrase deliberately excluded (Task 7), debounced push / periodic pull / conflict-retry-once / dirty-guarded pull-apply (Task 8), first-time setup UI (Task 9), and wiring + startup re-unlock + pull-on-open (Task 10).
- **Beyond the literal spec text, by necessity:** a local `sync-state.json` (the spec's "client tracks last_known_version per group id" isn't possible across app restarts without persisting it somewhere); the `ApplyRemoteGroupAsync` extension point (nothing in the existing Groups architecture can upsert arbitrary decrypted content by id); the "folders are not synced, fall back to Ungrouped" rule (the spec scoped this feature to Groups only and never mentioned syncing Folders as their own entity — expanding to sync Folders too was considered and deliberately rejected here as unrequested scope growth; documented as a limitation, not silently handled).
- **Type consistency check:** `IGroupSyncApiClient.PushGroupAsync`/`DeleteGroupAsync` return `int` (server-assigned version) in both Task 6's interface and every Task 8 call site; `GroupSyncStateEntry.SyncedVersion` is `int` throughout; `GroupSyncPullResult.LatestVersion` and `GroupSyncState.GlobalLastPulledVersion` are both `long` throughout (matches the server's SQLite `INTEGER` version counter, which the client only ever reads back as an opaque high-water mark, never arithmetic on it beyond `Math.Max`). `ArchivedGroupEntryInfo` field names/order used in Tasks 2, 5, and 8's tests match the existing record in `src/Clipt/Models/ArchivedGroupEntryInfo.cs` exactly.
