using System.Collections.Immutable;
using System.IO;
using System.Text;
using Clipt.Models;
using Clipt.Native;
using Clipt.Services;
using Moq;

namespace Clipt.Tests.Services;

public class ClipboardHistoryServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Mock<ISettingsService> _settingsMock;

    public ClipboardHistoryServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CliptTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _settingsMock = new Mock<ISettingsService>();
        _settingsMock.Setup(s => s.LoadMaxHistoryEntries()).Returns(10);
        _settingsMock.Setup(s => s.LoadMaxHistorySizeBytes()).Returns(100L * 1024 * 1024);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException) { }
    }

    private ClipboardHistoryService CreateService() =>
        new(_settingsMock.Object, _tempDir);

    private static ClipboardSnapshot CreateTextSnapshot(
        string text, uint seqNum = 1, DateTime? timestamp = null)
    {
        byte[] data = Encoding.Unicode.GetBytes(text + "\0");
        return new ClipboardSnapshot
        {
            Timestamp = timestamp ?? DateTime.UtcNow,
            SequenceNumber = seqNum,
            OwnerProcessName = "test",
            OwnerProcessId = 1,
            Formats = ImmutableArray.Create(new ClipboardFormatInfo
            {
                FormatId = ClipboardConstants.CF_UNICODETEXT,
                FormatName = "CF_UNICODETEXT",
                IsStandard = true,
                DataSize = data.Length,
                Memory = new MemoryInfo("0x0", "0x0", data.Length, []),
                RawData = data,
            }),
        };
    }

    [Fact]
    public async Task AddAsync_PersistsToDisk()
    {
        using var svc = CreateService();
        await svc.LoadAsync();

        await svc.AddAsync(CreateTextSnapshot("Hello"));

        Assert.Single(svc.Entries);
        Assert.Equal("Hello", svc.Entries[0].Summary);

        string indexPath = Path.Combine(_tempDir, "index.json");
        Assert.True(File.Exists(indexPath));

        string blobPath = Path.Combine(_tempDir, "blobs", svc.Entries[0].Id + ".bin");
        Assert.True(File.Exists(blobPath));
    }

    [Fact]
    public async Task AddAsync_SkipsDuplicateSequenceNumber()
    {
        using var svc = CreateService();
        await svc.LoadAsync();

        await svc.AddAsync(CreateTextSnapshot("First", seqNum: 42));
        await svc.AddAsync(CreateTextSnapshot("Second", seqNum: 42));

        Assert.Single(svc.Entries);
        Assert.Equal("First", svc.Entries[0].Summary);
    }

    [Fact]
    public async Task AddAsync_DifferentSequenceNumbers_AddsBoth()
    {
        using var svc = CreateService();
        await svc.LoadAsync();

        var t0 = DateTime.UtcNow.AddSeconds(-10);
        await svc.AddAsync(CreateTextSnapshot("First", seqNum: 1, timestamp: t0));
        await svc.AddAsync(CreateTextSnapshot("Second", seqNum: 2, timestamp: t0.AddSeconds(3)));

        Assert.Equal(2, svc.Entries.Count);
        Assert.Equal("Second", svc.Entries[0].Summary);
        Assert.Equal("First", svc.Entries[1].Summary);
    }

    [Fact]
    public async Task AddAsync_EntryCountCap_EvictsOldest()
    {
        _settingsMock.Setup(s => s.LoadMaxHistoryEntries()).Returns(3);
        using var svc = CreateService();
        await svc.LoadAsync();

        var t0 = DateTime.UtcNow.AddSeconds(-30);
        for (uint i = 1; i <= 5; i++)
            await svc.AddAsync(CreateTextSnapshot($"Item{i}", seqNum: i,
                timestamp: t0.AddSeconds(i * 3)));

        Assert.Equal(3, svc.Entries.Count);
        Assert.Equal("Item5", svc.Entries[0].Summary);
        Assert.Equal("Item4", svc.Entries[1].Summary);
        Assert.Equal("Item3", svc.Entries[2].Summary);
    }

    [Fact]
    public async Task AddAsync_SizeCap_EvictsOldest()
    {
        _settingsMock.Setup(s => s.LoadMaxHistoryEntries()).Returns(100);
        _settingsMock.Setup(s => s.LoadMaxHistorySizeBytes()).Returns(200L);

        using var svc = CreateService();
        await svc.LoadAsync();

        var t0 = DateTime.UtcNow.AddSeconds(-60);
        string largeText = new string('A', 50);
        for (uint i = 1; i <= 10; i++)
            await svc.AddAsync(CreateTextSnapshot(largeText + i, seqNum: i,
                timestamp: t0.AddSeconds(i * 3)));

        long totalSize = svc.Entries.Sum(e => e.DataSizeBytes);
        Assert.True(totalSize <= 200 || svc.Entries.Count == 1,
            $"Total size {totalSize} should be <= 200 or only 1 entry should remain");
    }

    [Fact]
    public async Task AddAsync_EmptySnapshot_IsIgnored()
    {
        using var svc = CreateService();
        await svc.LoadAsync();

        await svc.AddAsync(ClipboardSnapshot.Empty);

        Assert.Empty(svc.Entries);
    }

    [Fact]
    public async Task LoadAsync_RestoresFromDisk()
    {
        string entryId;
        {
            using var svc1 = CreateService();
            await svc1.LoadAsync();
            await svc1.AddAsync(CreateTextSnapshot("Persisted"));
            entryId = svc1.Entries[0].Id;
        }

        using var svc2 = CreateService();
        await svc2.LoadAsync();

        Assert.Single(svc2.Entries);
        Assert.Equal(entryId, svc2.Entries[0].Id);
        Assert.Equal("Persisted", svc2.Entries[0].Summary);
    }

    [Fact]
    public async Task RestoreAsync_ReadsBlob_ReturnsSnapshot()
    {
        using var svc = CreateService();
        await svc.LoadAsync();
        await svc.AddAsync(CreateTextSnapshot("Restore Me", seqNum: 7));

        var entry = svc.Entries[0];
        var restored = await svc.RestoreAsync(entry.Id);

        Assert.NotNull(restored);
        Assert.Equal(7u, restored.SequenceNumber);
        Assert.Single(restored.Formats);
        Assert.Equal(ClipboardConstants.CF_UNICODETEXT, restored.Formats[0].FormatId);

        string text = Encoding.Unicode.GetString(restored.Formats[0].RawData).TrimEnd('\0');
        Assert.Equal("Restore Me", text);
    }

    [Fact]
    public async Task RestoreAsync_UnknownId_ReturnsNull()
    {
        using var svc = CreateService();
        await svc.LoadAsync();

        var result = await svc.RestoreAsync("nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public async Task RemoveAsync_DeletesEntryAndBlob()
    {
        using var svc = CreateService();
        await svc.LoadAsync();
        await svc.AddAsync(CreateTextSnapshot("ToDelete", seqNum: 1));

        string id = svc.Entries[0].Id;
        string blobPath = Path.Combine(_tempDir, "blobs", id + ".bin");
        Assert.True(File.Exists(blobPath));

        await svc.RemoveAsync(id);

        Assert.Empty(svc.Entries);
        Assert.False(File.Exists(blobPath));
    }

    [Fact]
    public async Task ClearAsync_RemovesAllEntriesAndBlobs()
    {
        using var svc = CreateService();
        await svc.LoadAsync();

        var t0 = DateTime.UtcNow.AddSeconds(-20);
        for (uint i = 1; i <= 3; i++)
            await svc.AddAsync(CreateTextSnapshot($"Item{i}", seqNum: i,
                timestamp: t0.AddSeconds(i * 3)));

        Assert.Equal(3, svc.Entries.Count);

        await svc.ClearAsync();

        Assert.Empty(svc.Entries);

        string blobsDir = Path.Combine(_tempDir, "blobs");
        var remaining = Directory.GetFiles(blobsDir, "*.bin");
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task LoadAsync_CorruptIndex_StartsEmpty()
    {
        Directory.CreateDirectory(_tempDir);
        string indexPath = Path.Combine(_tempDir, "index.json");
        await File.WriteAllTextAsync(indexPath, "{{{{not json");

        using var svc = CreateService();
        await svc.LoadAsync();

        Assert.Empty(svc.Entries);
    }

    [Fact]
    public async Task LoadAsync_MissingIndex_StartsEmpty()
    {
        using var svc = CreateService();
        await svc.LoadAsync();

        Assert.Empty(svc.Entries);
    }

    [Fact]
    public async Task AddAsync_RaisesEntriesChanged()
    {
        using var svc = CreateService();
        await svc.LoadAsync();

        bool raised = false;
        svc.EntriesChanged += (_, _) => raised = true;

        await svc.AddAsync(CreateTextSnapshot("Event"));
        Assert.True(raised);
    }

    [Fact]
    public async Task ClearAsync_RaisesEntriesChanged()
    {
        using var svc = CreateService();
        await svc.LoadAsync();
        await svc.AddAsync(CreateTextSnapshot("ToRemove", seqNum: 1));

        bool raised = false;
        svc.EntriesChanged += (_, _) => raised = true;

        await svc.ClearAsync();
        Assert.True(raised);
    }

    [Fact]
    public void SerializeDeserialize_RoundTrips()
    {
        var snapshot = CreateTextSnapshot("RoundTrip", seqNum: 42);
        byte[] blob = ClipboardHistoryService.SerializeSnapshot(snapshot);

        var entry = new ClipboardHistoryEntry
        {
            Id = "test",
            TimestampUtc = snapshot.Timestamp,
            SequenceNumber = 42,
            OwnerProcess = "test",
            OwnerPid = 1,
            Summary = "RoundTrip",
            ContentType = ContentType.Text,
            DataSizeBytes = blob.Length,
        };

        var restored = ClipboardHistoryService.DeserializeSnapshot(blob, entry);

        Assert.Equal(42u, restored.SequenceNumber);
        Assert.Single(restored.Formats);
        Assert.Equal(ClipboardConstants.CF_UNICODETEXT, restored.Formats[0].FormatId);
        Assert.Equal("CF_UNICODETEXT", restored.Formats[0].FormatName);
        Assert.True(restored.Formats[0].IsStandard);
    }

    [Fact]
    public void DetermineContentType_Text()
    {
        var snapshot = CreateTextSnapshot("Hello");
        Assert.Equal(ContentType.Text, ClipboardHistoryService.DetermineContentType(snapshot));
    }

    [Fact]
    public void DetermineContentType_EmptyFormats()
    {
        Assert.Equal(ContentType.Empty, ClipboardHistoryService.DetermineContentType(ClipboardSnapshot.Empty));
    }

    [Fact]
    public void BuildSummary_TextSnapshot_ReturnsTextContent()
    {
        var snapshot = CreateTextSnapshot("Hello World");
        string summary = ClipboardHistoryService.BuildSummary(snapshot);
        Assert.Equal("Hello World", summary);
    }

    [Fact]
    public void BuildSummary_LongText_Truncates()
    {
        var snapshot = CreateTextSnapshot(new string('X', 200));
        string summary = ClipboardHistoryService.BuildSummary(snapshot);
        Assert.Equal(83, summary.Length);
        Assert.EndsWith("...", summary);
    }

    [Fact]
    public async Task Dispose_PreventsSubsequentOperations()
    {
        var svc = CreateService();
        await svc.LoadAsync();
        svc.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            svc.AddAsync(CreateTextSnapshot("After dispose")));
    }

    [Fact]
    public async Task AddAsync_WhenSuppressed_DoesNotAddEntry()
    {
        using var svc = CreateService();
        await svc.LoadAsync();

        svc.IsSuppressed = true;
        await svc.AddAsync(CreateTextSnapshot("Suppressed", seqNum: 1));

        Assert.Empty(svc.Entries);
    }

    [Fact]
    public async Task AddAsync_WhenSuppressed_DoesNotRaiseEvent()
    {
        using var svc = CreateService();
        await svc.LoadAsync();

        bool raised = false;
        svc.EntriesChanged += (_, _) => raised = true;

        svc.IsSuppressed = true;
        await svc.AddAsync(CreateTextSnapshot("Suppressed", seqNum: 1));

        Assert.False(raised);
    }

    [Fact]
    public async Task AddAsync_AfterSuppressionCleared_AddsNormally()
    {
        using var svc = CreateService();
        await svc.LoadAsync();

        svc.IsSuppressed = true;
        await svc.AddAsync(CreateTextSnapshot("Suppressed", seqNum: 1));

        svc.IsSuppressed = false;
        await svc.AddAsync(CreateTextSnapshot("NotSuppressed", seqNum: 2));

        Assert.Single(svc.Entries);
        Assert.Equal("NotSuppressed", svc.Entries[0].Summary);
    }

    [Fact]
    public async Task AddAsync_DifferentSeqNum_SameContent_Rejected()
    {
        using var svc = CreateService();
        await svc.LoadAsync();

        await svc.AddAsync(CreateTextSnapshot("Hello", seqNum: 1));
        await svc.AddAsync(CreateTextSnapshot("Hello", seqNum: 2));

        Assert.Single(svc.Entries);
    }

    [Fact]
    public async Task AddAsync_SameContent_BeyondLookback_Accepted()
    {
        using var svc = CreateService();
        await svc.LoadAsync();

        var t0 = DateTime.UtcNow.AddSeconds(-60);
        await svc.AddAsync(CreateTextSnapshot("Repeat", seqNum: 1, timestamp: t0));

        for (uint i = 2; i <= (uint)ClipboardHistoryService.ContentHashLookback + 1; i++)
            await svc.AddAsync(CreateTextSnapshot($"Filler{i}", seqNum: i,
                timestamp: t0.AddSeconds(i * 3)));

        uint nextSeq = (uint)ClipboardHistoryService.ContentHashLookback + 2;
        await svc.AddAsync(CreateTextSnapshot("Repeat", seqNum: nextSeq,
            timestamp: t0.AddSeconds(nextSeq * 3)));

        Assert.Equal(ClipboardHistoryService.ContentHashLookback + 2, svc.Entries.Count);
        Assert.Equal("Repeat", svc.Entries[0].Summary);
    }

    [Fact]
    public async Task AddAsync_ContentHash_PersistedAndRestoredOnLoad()
    {
        string hash;
        {
            using var svc1 = CreateService();
            await svc1.LoadAsync();
            await svc1.AddAsync(CreateTextSnapshot("HashTest", seqNum: 1));
            hash = svc1.Entries[0].ContentHash;
            Assert.False(string.IsNullOrEmpty(hash));
        }

        using var svc2 = CreateService();
        await svc2.LoadAsync();
        Assert.Equal(hash, svc2.Entries[0].ContentHash);
    }

    [Fact]
    public void ComputeContentHash_EmptySnapshot_ReturnsEmpty()
    {
        string hash = ClipboardHistoryService.ComputeContentHash(ClipboardSnapshot.Empty);
        Assert.Equal(string.Empty, hash);
    }

    [Fact]
    public void ComputeContentHash_SameContent_SameHash()
    {
        var a = CreateTextSnapshot("Identical", seqNum: 1);
        var b = CreateTextSnapshot("Identical", seqNum: 99);
        Assert.Equal(
            ClipboardHistoryService.ComputeContentHash(a),
            ClipboardHistoryService.ComputeContentHash(b));
    }

    [Fact]
    public void ComputeContentHash_DifferentContent_DifferentHash()
    {
        var a = CreateTextSnapshot("Alpha", seqNum: 1);
        var b = CreateTextSnapshot("Beta", seqNum: 1);
        Assert.NotEqual(
            ClipboardHistoryService.ComputeContentHash(a),
            ClipboardHistoryService.ComputeContentHash(b));
    }

    [Fact]
    public async Task IsSuppressed_ThreadSafety()
    {
        using var svc = CreateService();
        await svc.LoadAsync();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        bool observedTrue = false;

        var writer = Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                svc.IsSuppressed = true;
                svc.IsSuppressed = false;
            }
        }, cts.Token);

        var reader = Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                bool val = svc.IsSuppressed;
                if (val) observedTrue = true;
            }
        }, cts.Token);

        try { await Task.WhenAll(writer, reader); }
        catch (OperationCanceledException) { }

        Assert.True(observedTrue, "Reader should observe IsSuppressed=true at least once");
        Assert.False(svc.IsSuppressed, "IsSuppressed should be false after writer finishes");
    }

    [Fact]
    public async Task AddAsync_SameContentType_WithinWindow_ReplacesHead()
    {
        using var svc = CreateService();
        await svc.LoadAsync();

        await svc.AddAsync(CreateTextSnapshot("First", seqNum: 1));
        Assert.Single(svc.Entries);

        await svc.AddAsync(CreateTextSnapshot("Second", seqNum: 2));
        Assert.Single(svc.Entries);
        Assert.Equal("Second", svc.Entries[0].Summary);
    }

    [Fact]
    public async Task AddAsync_DifferentContentType_WithinWindow_AddsBoth()
    {
        using var svc = CreateService();
        await svc.LoadAsync();

        await svc.AddAsync(CreateTextSnapshot("Text", seqNum: 1));

        var imageSnapshot = CreateImageSnapshot(seqNum: 2);
        await svc.AddAsync(imageSnapshot);

        Assert.Equal(2, svc.Entries.Count);
    }

    [Fact]
    public async Task AddAsync_SameContentType_OutsideWindow_AddsBoth()
    {
        using var svc = CreateService();
        await svc.LoadAsync();

        var oldSnapshot = new ClipboardSnapshot
        {
            Timestamp = DateTime.UtcNow.AddSeconds(-5),
            SequenceNumber = 1,
            OwnerProcessName = "test",
            OwnerProcessId = 1,
            Formats = System.Collections.Immutable.ImmutableArray.Create(new ClipboardFormatInfo
            {
                FormatId = ClipboardConstants.CF_UNICODETEXT,
                FormatName = "CF_UNICODETEXT",
                IsStandard = true,
                DataSize = 12,
                Memory = new MemoryInfo("0x0", "0x0", 12, []),
                RawData = Encoding.Unicode.GetBytes("Old\0"),
            }),
        };
        await svc.AddAsync(oldSnapshot);

        await svc.AddAsync(CreateTextSnapshot("New", seqNum: 2));
        Assert.Equal(2, svc.Entries.Count);
    }

    private static ClipboardSnapshot CreateImageSnapshot(uint seqNum = 1)
    {
        byte[] dibHeader = new byte[40];
        dibHeader[0] = 40; // biSize
        return new ClipboardSnapshot
        {
            Timestamp = DateTime.UtcNow,
            SequenceNumber = seqNum,
            OwnerProcessName = "test",
            OwnerProcessId = 1,
            Formats = System.Collections.Immutable.ImmutableArray.Create(new ClipboardFormatInfo
            {
                FormatId = ClipboardConstants.CF_DIB,
                FormatName = "CF_DIB",
                IsStandard = true,
                DataSize = dibHeader.Length,
                Memory = new MemoryInfo("0x0", "0x0", dibHeader.Length, []),
                RawData = dibHeader,
            }),
        };
    }

    private static ClipboardSnapshot CreateImageSnapshotWithDimensions(
        int width, int height, uint seqNum = 1)
    {
        byte[] dibHeader = new byte[40];
        BitConverter.TryWriteBytes(dibHeader.AsSpan(0, 4), 40);   // biSize
        BitConverter.TryWriteBytes(dibHeader.AsSpan(4, 4), width);  // biWidth
        BitConverter.TryWriteBytes(dibHeader.AsSpan(8, 4), height); // biHeight

        return new ClipboardSnapshot
        {
            Timestamp = DateTime.UtcNow,
            SequenceNumber = seqNum,
            OwnerProcessName = "test",
            OwnerProcessId = 1,
            Formats = System.Collections.Immutable.ImmutableArray.Create(new ClipboardFormatInfo
            {
                FormatId = ClipboardConstants.CF_DIB,
                FormatName = "CF_DIB",
                IsStandard = true,
                DataSize = dibHeader.Length,
                Memory = new MemoryInfo("0x0", "0x0", dibHeader.Length, []),
                RawData = dibHeader,
            }),
        };
    }

    private static byte[] BuildHdropWide(params string[] paths)
    {
        const int headerSize = 20;
        using var ms = new MemoryStream();

        ms.Write(BitConverter.GetBytes(headerSize), 0, 4); // pFiles offset
        ms.Write(new byte[4], 0, 4);                       // pt.x
        ms.Write(new byte[4], 0, 4);                       // pt.y
        ms.Write(new byte[4], 0, 4);                       // fNC
        ms.Write(BitConverter.GetBytes(1), 0, 4);           // fWide = true

        foreach (var path in paths)
        {
            byte[] encoded = Encoding.Unicode.GetBytes(path);
            ms.Write(encoded, 0, encoded.Length);
            ms.Write(new byte[2], 0, 2); // null terminator (wide)
        }

        ms.Write(new byte[2], 0, 2); // double null terminator
        return ms.ToArray();
    }

    private static byte[] BuildHdropAnsi(params string[] paths)
    {
        const int headerSize = 20;
        using var ms = new MemoryStream();

        ms.Write(BitConverter.GetBytes(headerSize), 0, 4); // pFiles offset
        ms.Write(new byte[4], 0, 4);                       // pt.x
        ms.Write(new byte[4], 0, 4);                       // pt.y
        ms.Write(new byte[4], 0, 4);                       // fNC
        ms.Write(BitConverter.GetBytes(0), 0, 4);           // fWide = false

        foreach (var path in paths)
        {
            byte[] encoded = Encoding.Default.GetBytes(path);
            ms.Write(encoded, 0, encoded.Length);
            ms.WriteByte(0); // null terminator
        }

        ms.WriteByte(0); // double null terminator
        return ms.ToArray();
    }

    private static ClipboardSnapshot CreateFileDropSnapshot(byte[] hdropData, uint seqNum = 1)
    {
        return new ClipboardSnapshot
        {
            Timestamp = DateTime.UtcNow,
            SequenceNumber = seqNum,
            OwnerProcessName = "test",
            OwnerProcessId = 1,
            Formats = System.Collections.Immutable.ImmutableArray.Create(new ClipboardFormatInfo
            {
                FormatId = ClipboardConstants.CF_HDROP,
                FormatName = "CF_HDROP",
                IsStandard = true,
                DataSize = hdropData.Length,
                Memory = new MemoryInfo("0x0", "0x0", hdropData.Length, []),
                RawData = hdropData,
            }),
        };
    }

    [Fact]
    public void BuildSummary_SingleFileHdrop_ReturnsFilename()
    {
        byte[] hdrop = BuildHdropWide(@"C:\Users\Test\Documents\report.pdf");
        var snapshot = CreateFileDropSnapshot(hdrop);
        string summary = ClipboardHistoryService.BuildSummary(snapshot);
        Assert.Equal("report.pdf", summary);
    }

    [Fact]
    public void BuildSummary_MultipleFilesHdrop_ReturnsFirstPlusCount()
    {
        byte[] hdrop = BuildHdropWide(
            @"C:\Photos\sunset.jpg",
            @"C:\Photos\mountain.png",
            @"C:\Photos\river.bmp");
        var snapshot = CreateFileDropSnapshot(hdrop);
        string summary = ClipboardHistoryService.BuildSummary(snapshot);
        Assert.Equal("sunset.jpg +2 more", summary);
    }

    [Fact]
    public void BuildSummary_ImageWithDimensions_ReturnsDimensionString()
    {
        var snapshot = CreateImageSnapshotWithDimensions(1920, 1080);
        string summary = ClipboardHistoryService.BuildSummary(snapshot);
        Assert.Equal("1920\u00D71080", summary);
    }

    [Fact]
    public void BuildSummary_ImageWithNegativeHeight_ReturnsAbsoluteDimensions()
    {
        var snapshot = CreateImageSnapshotWithDimensions(800, -600);
        string summary = ClipboardHistoryService.BuildSummary(snapshot);
        Assert.Equal("800\u00D7600", summary);
    }

    [Fact]
    public void BuildSummary_ImageWithZeroDimensions_FallsBackToImage()
    {
        var snapshot = CreateImageSnapshotWithDimensions(0, 0);
        string summary = ClipboardHistoryService.BuildSummary(snapshot);
        Assert.Equal("Image", summary);
    }

    [Fact]
    public void ParseDropFileNames_Wide_ParsesCorrectly()
    {
        byte[] hdrop = BuildHdropWide(
            @"C:\a.txt",
            @"D:\folder\b.doc");
        var names = ClipboardHistoryService.ParseDropFileNames(hdrop);
        Assert.Equal(2, names.Count);
        Assert.Equal(@"C:\a.txt", names[0]);
        Assert.Equal(@"D:\folder\b.doc", names[1]);
    }

    [Fact]
    public void ParseDropFileNames_Ansi_ParsesCorrectly()
    {
        byte[] hdrop = BuildHdropAnsi(@"C:\test.txt");
        var names = ClipboardHistoryService.ParseDropFileNames(hdrop);
        Assert.Single(names);
        Assert.Equal(@"C:\test.txt", names[0]);
    }

    [Fact]
    public void ParseDropFileNames_TooSmallData_ReturnsEmpty()
    {
        var names = ClipboardHistoryService.ParseDropFileNames(new byte[5]);
        Assert.Empty(names);
    }

    [Fact]
    public void ParseDropFileNames_InvalidOffset_ReturnsEmpty()
    {
        byte[] data = new byte[20];
        BitConverter.TryWriteBytes(data.AsSpan(0, 4), 9999); // offset beyond data
        var names = ClipboardHistoryService.ParseDropFileNames(data);
        Assert.Empty(names);
    }

    [Fact]
    public void ExtractImageDimensions_ValidHeader_ReturnsDimensions()
    {
        byte[] dib = new byte[40];
        BitConverter.TryWriteBytes(dib.AsSpan(0, 4), 40);
        BitConverter.TryWriteBytes(dib.AsSpan(4, 4), 2560);
        BitConverter.TryWriteBytes(dib.AsSpan(8, 4), 1440);
        string dims = ClipboardHistoryService.ExtractImageDimensions(dib);
        Assert.Equal("2560\u00D71440", dims);
    }

    [Fact]
    public void ExtractImageDimensions_NegativeHeight_ReturnsAbsoluteValue()
    {
        byte[] dib = new byte[40];
        BitConverter.TryWriteBytes(dib.AsSpan(0, 4), 40);
        BitConverter.TryWriteBytes(dib.AsSpan(4, 4), 100);
        BitConverter.TryWriteBytes(dib.AsSpan(8, 4), -200);
        string dims = ClipboardHistoryService.ExtractImageDimensions(dib);
        Assert.Equal("100\u00D7200", dims);
    }

    [Fact]
    public void ExtractImageDimensions_TooSmallData_ReturnsEmpty()
    {
        string dims = ClipboardHistoryService.ExtractImageDimensions(new byte[8]);
        Assert.Equal(string.Empty, dims);
    }

    [Fact]
    public void ExtractImageDimensions_ZeroDimensions_ReturnsEmpty()
    {
        byte[] dib = new byte[40];
        BitConverter.TryWriteBytes(dib.AsSpan(0, 4), 40);
        string dims = ClipboardHistoryService.ExtractImageDimensions(dib);
        Assert.Equal(string.Empty, dims);
    }

    [Fact]
    public void FormatFileNames_EmptyList_ReturnsZeroFiles()
    {
        string result = ClipboardHistoryService.FormatFileNames([]);
        Assert.Equal("0 files", result);
    }

    [Fact]
    public void FormatFileNames_SingleFile_ReturnsNameOnly()
    {
        string result = ClipboardHistoryService.FormatFileNames([@"C:\folder\readme.md"]);
        Assert.Equal("readme.md", result);
    }

    [Fact]
    public void FormatFileNames_MultipleFiles_ReturnsFirstPlusCount()
    {
        string result = ClipboardHistoryService.FormatFileNames([
            @"C:\a.txt", @"C:\b.txt", @"C:\c.txt"]);
        Assert.Equal("a.txt +2 more", result);
    }
}
