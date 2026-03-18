using System.Collections.Immutable;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clipt.Models;
using Clipt.Native;

namespace Clipt.Services;

public sealed class ClipboardHistoryService : IClipboardHistoryService
{
    internal const int ContentHashLookback = 5;
    internal static readonly TimeSpan DeduplicateWindow = TimeSpan.FromSeconds(2);

    private readonly ISettingsService _settingsService;
    private readonly string _historyDir;
    private readonly string _blobsDir;
    private readonly string _indexPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<ClipboardHistoryEntry> _entries = [];
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public event EventHandler? EntriesChanged;

    public IReadOnlyList<ClipboardHistoryEntry> Entries => _entries.AsReadOnly();

    private int _suppressed;

    public bool IsSuppressed
    {
        get => Interlocked.CompareExchange(ref _suppressed, 0, 0) != 0;
        set => Interlocked.Exchange(ref _suppressed, value ? 1 : 0);
    }

    public ClipboardHistoryService(ISettingsService settingsService)
        : this(settingsService, GetDefaultHistoryDirectory())
    {
    }

    internal ClipboardHistoryService(ISettingsService settingsService, string historyDirectory)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _historyDir = historyDirectory ?? throw new ArgumentNullException(nameof(historyDirectory));
        _blobsDir = Path.Combine(_historyDir, "blobs");
        _indexPath = Path.Combine(_historyDir, "index.json");
    }

    public async Task LoadAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _entries.Clear();
            EnsureDirectoriesExist();

            if (!File.Exists(_indexPath))
                return;

            byte[] json;
            try
            {
                json = await File.ReadAllBytesAsync(_indexPath).ConfigureAwait(false);
            }
            catch (IOException)
            {
                return;
            }

            IndexFile? index;
            try
            {
                index = JsonSerializer.Deserialize<IndexFile>(json, JsonOptions);
            }
            catch (JsonException)
            {
                return;
            }

            if (index?.Entries is null)
                return;

            foreach (var ie in index.Entries)
            {
                if (string.IsNullOrEmpty(ie.Id))
                    continue;

                _entries.Add(new ClipboardHistoryEntry
                {
                    Id = ie.Id,
                    Name = ie.Name ?? BuildDefaultNameFromIndex(ie),
                    TimestampUtc = ie.TimestampUtc,
                    SequenceNumber = ie.SequenceNumber,
                    OwnerProcess = ie.OwnerProcess ?? "(unknown)",
                    OwnerPid = ie.OwnerPid,
                    Summary = ie.Summary ?? string.Empty,
                    ContentType = ie.ContentType,
                    DataSizeBytes = ie.DataSizeBytes,
                    ContentHash = ie.ContentHash ?? string.Empty,
                });
            }
        }
        finally
        {
            _gate.Release();
        }

        EntriesChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task AddAsync(ClipboardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (IsSuppressed)
            return;

        if (snapshot.Formats.Length == 0)
            return;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_entries.Count > 0 && _entries[0].SequenceNumber == snapshot.SequenceNumber)
                return;

            string contentHash = ComputeContentHash(snapshot);

            int lookback = Math.Min(_entries.Count, ContentHashLookback);
            for (int i = 0; i < lookback; i++)
            {
                if (_entries[i].ContentHash.Length > 0
                    && _entries[i].ContentHash == contentHash)
                    return;
            }

            ContentType newContentType = DetermineContentType(snapshot);

            var disabledTypes = _settingsService.LoadDisabledHistoryTypes();
            if (disabledTypes.Contains(newContentType))
                return;

            string id = Guid.NewGuid().ToString("N");

            EnsureDirectoriesExist();

            byte[] blobData = SerializeSnapshot(snapshot);

            if (_entries.Count > 0)
            {
                var head = _entries[0];
                TimeSpan age = snapshot.Timestamp - head.TimestampUtc;
                if (age < DeduplicateWindow && head.ContentType == newContentType)
                {
                    DeleteBlobQuietly(head.Id);
                    _entries.RemoveAt(0);
                }
            }

            string blobPath = GetBlobPath(id);
            await File.WriteAllBytesAsync(blobPath, blobData).ConfigureAwait(false);

            var entry = new ClipboardHistoryEntry
            {
                Id = id,
                Name = BuildDefaultName(snapshot, newContentType),
                TimestampUtc = snapshot.Timestamp,
                SequenceNumber = snapshot.SequenceNumber,
                OwnerProcess = snapshot.OwnerProcessName,
                OwnerPid = snapshot.OwnerProcessId,
                Summary = BuildSummary(snapshot),
                ContentType = newContentType,
                DataSizeBytes = blobData.LongLength,
                ContentHash = contentHash,
            };

            _entries.Insert(0, entry);
            EnforceCaps();
            await WriteIndexAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        EntriesChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task RemoveAsync(string entryId)
    {
        ArgumentException.ThrowIfNullOrEmpty(entryId);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            int idx = _entries.FindIndex(e => e.Id == entryId);
            if (idx < 0)
                return;

            DeleteBlobQuietly(_entries[idx].Id);
            _entries.RemoveAt(idx);
            await WriteIndexAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        EntriesChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task RenameAsync(string entryId, string newName)
    {
        ArgumentException.ThrowIfNullOrEmpty(entryId);
        ArgumentNullException.ThrowIfNull(newName);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var entry = _entries.FirstOrDefault(e => e.Id == entryId);
            if (entry is null)
                return;

            entry.Name = newName;
            await WriteIndexAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        EntriesChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task PromoteAsync(string entryId)
    {
        ArgumentException.ThrowIfNullOrEmpty(entryId);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            int idx = _entries.FindIndex(e => e.Id == entryId);
            if (idx <= 0)
                return;

            var entry = _entries[idx];
            _entries.RemoveAt(idx);
            _entries.Insert(0, entry);
            await WriteIndexAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        EntriesChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task ClearAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var entry in _entries)
                DeleteBlobQuietly(entry.Id);

            _entries.Clear();
            await WriteIndexAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        EntriesChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<ClipboardSnapshot?> RestoreAsync(string entryId)
    {
        ArgumentException.ThrowIfNullOrEmpty(entryId);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var entry = _entries.FirstOrDefault(e => e.Id == entryId);
            if (entry is null)
                return null;

            string blobPath = GetBlobPath(entry.Id);
            if (!File.Exists(blobPath))
                return null;

            byte[] blobData = await File.ReadAllBytesAsync(blobPath).ConfigureAwait(false);
            try
            {
                return DeserializeSnapshot(blobData, entry);
            }
            catch (Exception ex) when (
                ex is InvalidDataException or EndOfStreamException or IOException)
            {
                return null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static string ComputeContentHash(ClipboardSnapshot snapshot)
    {
        if (snapshot.Formats.Length == 0)
            return string.Empty;

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> idBytes = stackalloc byte[4];

        foreach (var fmt in snapshot.Formats)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(idBytes, fmt.FormatId);
            sha.AppendData(idBytes);

            if (fmt.RawData.Length > 0)
                sha.AppendData(fmt.RawData);
        }

        byte[] hash = sha.GetHashAndReset();
        return Convert.ToHexString(hash);
    }

    private void EnforceCaps()
    {
        int maxEntries = _settingsService.LoadMaxHistoryEntries();
        long maxSizeBytes = _settingsService.LoadMaxHistorySizeBytes();

        while (_entries.Count > maxEntries)
        {
            var oldest = _entries[^1];
            DeleteBlobQuietly(oldest.Id);
            _entries.RemoveAt(_entries.Count - 1);
        }

        if (maxSizeBytes > 0)
        {
            while (_entries.Count > 1 && ComputeTotalSize() > maxSizeBytes)
            {
                var oldest = _entries[^1];
                DeleteBlobQuietly(oldest.Id);
                _entries.RemoveAt(_entries.Count - 1);
            }
        }
    }

    private long ComputeTotalSize()
    {
        long total = 0;
        foreach (var e in _entries)
            total += e.DataSizeBytes;
        return total;
    }

    private async Task WriteIndexAsync()
    {
        var indexFile = new IndexFile
        {
            Entries = _entries.Select(e => new IndexEntry
            {
                Id = e.Id,
                Name = e.Name,
                TimestampUtc = e.TimestampUtc,
                SequenceNumber = e.SequenceNumber,
                OwnerProcess = e.OwnerProcess,
                OwnerPid = e.OwnerPid,
                Summary = e.Summary,
                ContentType = e.ContentType,
                DataSizeBytes = e.DataSizeBytes,
                ContentHash = e.ContentHash,
            }).ToList(),
        };

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(indexFile, JsonOptions);

        string tmpPath = _indexPath + ".tmp";
        await File.WriteAllBytesAsync(tmpPath, json).ConfigureAwait(false);
        File.Move(tmpPath, _indexPath, overwrite: true);
    }

    internal static byte[] SerializeSnapshot(ClipboardSnapshot snapshot)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

        writer.Write(snapshot.Formats.Length);

        foreach (var fmt in snapshot.Formats)
        {
            writer.Write(fmt.FormatId);

            byte[] nameBytes = Encoding.UTF8.GetBytes(fmt.FormatName);
            writer.Write(nameBytes.Length);
            writer.Write(nameBytes);

            writer.Write(fmt.IsStandard ? (byte)1 : (byte)0);

            writer.Write(fmt.RawData.Length);
            if (fmt.RawData.Length > 0)
                writer.Write(fmt.RawData);
        }

        writer.Flush();
        return ms.ToArray();
    }

    internal static ClipboardSnapshot DeserializeSnapshot(byte[] data, ClipboardHistoryEntry entry)
    {
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        int formatCount = reader.ReadInt32();
        if (formatCount < 0 || formatCount > 10_000)
            throw new InvalidDataException($"Invalid format count {formatCount} in history blob.");

        var builder = ImmutableArray.CreateBuilder<ClipboardFormatInfo>(formatCount);

        for (int i = 0; i < formatCount; i++)
        {
            uint formatId = reader.ReadUInt32();

            int nameLen = reader.ReadInt32();
            if (nameLen < 0 || nameLen > 1_000)
                throw new InvalidDataException($"Invalid format name length {nameLen} in history blob.");
            byte[] nameBytes = reader.ReadBytes(nameLen);
            string formatName = Encoding.UTF8.GetString(nameBytes);

            bool isStandard = reader.ReadByte() != 0;

            int rawLen = reader.ReadInt32();
            if (rawLen < 0)
                throw new InvalidDataException($"Negative raw data length {rawLen} in history blob.");
            byte[] rawData = rawLen > 0 ? reader.ReadBytes(rawLen) : [];

            builder.Add(new ClipboardFormatInfo
            {
                FormatId = formatId,
                FormatName = formatName,
                IsStandard = isStandard,
                DataSize = rawData.Length,
                Memory = new MemoryInfo("0x0", "0x0", rawData.Length, []),
                RawData = rawData,
            });
        }

        return new ClipboardSnapshot
        {
            Timestamp = entry.TimestampUtc,
            SequenceNumber = entry.SequenceNumber,
            OwnerProcessName = entry.OwnerProcess,
            OwnerProcessId = entry.OwnerPid,
            Formats = builder.ToImmutable(),
        };
    }

    internal static ContentType DetermineContentType(ClipboardSnapshot snapshot)
    {
        if (snapshot.Formats.Length == 0)
            return ContentType.Empty;

        bool hasText = snapshot.Formats.Any(f => f.FormatId == ClipboardConstants.CF_UNICODETEXT);
        bool hasImage = snapshot.Formats.Any(f =>
            f.FormatId is ClipboardConstants.CF_DIB or ClipboardConstants.CF_DIBV5 or ClipboardConstants.CF_BITMAP);
        bool hasFiles = snapshot.Formats.Any(f => f.FormatId == ClipboardConstants.CF_HDROP);

        if (hasFiles) return ContentType.Files;
        if (hasImage) return ContentType.Image;
        if (hasText) return ContentType.Text;
        return ContentType.Other;
    }

    internal static string BuildSummary(ClipboardSnapshot snapshot)
    {
        const int maxSummaryLength = 80;

        var unicodeFormat = snapshot.Formats
            .FirstOrDefault(f => f.FormatId == ClipboardConstants.CF_UNICODETEXT);

        if (unicodeFormat is not null && unicodeFormat.RawData.Length > 0)
        {
            string text = DecodeUtf16Truncated(unicodeFormat.RawData, maxSummaryLength + 1);
            string singleLine = text.ReplaceLineEndings(" ");
            if (singleLine.Length > maxSummaryLength)
                return string.Concat(singleLine.AsSpan(0, maxSummaryLength), "...");
            return singleLine;
        }

        var hdropFormat = snapshot.Formats
            .FirstOrDefault(f => f.FormatId == ClipboardConstants.CF_HDROP);

        if (hdropFormat is not null && hdropFormat.RawData.Length >= 20)
        {
            var names = ParseDropFileNames(hdropFormat.RawData);
            return FormatFileNames(names);
        }

        var dibv5 = snapshot.Formats.FirstOrDefault(f => f.FormatId == ClipboardConstants.CF_DIBV5);
        var dib = snapshot.Formats.FirstOrDefault(f => f.FormatId == ClipboardConstants.CF_DIB);
        ClipboardFormatInfo? imageFormat = dibv5 ?? dib;

        if (imageFormat is not null && imageFormat.RawData.Length >= 16)
        {
            string dims = ExtractImageDimensions(imageFormat.RawData);
            return dims.Length > 0 ? dims : "Image";
        }

        bool hasBitmap = snapshot.Formats.Any(f => f.FormatId == ClipboardConstants.CF_BITMAP);
        if (hasBitmap)
            return "Image";

        return $"{snapshot.Formats.Length} format(s)";
    }

    internal static string BuildDefaultName(ClipboardSnapshot snapshot, ContentType contentType)
    {
        const int maxNameLength = 40;

        switch (contentType)
        {
            case ContentType.Text:
                var unicodeFormat = snapshot.Formats
                    .FirstOrDefault(f => f.FormatId == ClipboardConstants.CF_UNICODETEXT);
                if (unicodeFormat is not null && unicodeFormat.RawData.Length > 0)
                {
                    string text = DecodeUtf16Truncated(unicodeFormat.RawData, maxNameLength + 1).Trim();
                    string firstLine = text.Split('\n', 2)[0].Trim();
                    if (firstLine.Length > maxNameLength)
                        return string.Concat(firstLine.AsSpan(0, maxNameLength), "\u2026");
                    return firstLine.Length > 0 ? firstLine : "Text clip";
                }
                return "Text clip";

            case ContentType.Image:
                var dibv5 = snapshot.Formats.FirstOrDefault(f => f.FormatId == ClipboardConstants.CF_DIBV5);
                var dib = snapshot.Formats.FirstOrDefault(f => f.FormatId == ClipboardConstants.CF_DIB);
                ClipboardFormatInfo? imageFormat = dibv5 ?? dib;
                if (imageFormat is not null && imageFormat.RawData.Length >= 16)
                {
                    string dims = ExtractImageDimensions(imageFormat.RawData);
                    if (dims.Length > 0)
                        return $"Image {dims}";
                }
                return "Image";

            case ContentType.Files:
                var hdropFormat = snapshot.Formats
                    .FirstOrDefault(f => f.FormatId == ClipboardConstants.CF_HDROP);
                if (hdropFormat is not null && hdropFormat.RawData.Length >= 20)
                {
                    var names = ParseDropFileNames(hdropFormat.RawData);
                    if (names.Count == 1)
                    {
                        string fileName = Path.GetFileName(names[0]);
                        return fileName.Length > 0 ? fileName : "File";
                    }
                    if (names.Count > 1)
                        return $"{names.Count} files";
                }
                return "File drop";

            default:
                return $"Clip ({snapshot.Formats.Length} format{(snapshot.Formats.Length == 1 ? "" : "s")})";
        }
    }

    private static string BuildDefaultNameFromIndex(IndexEntry ie)
    {
        return ie.ContentType switch
        {
            ContentType.Text => TruncateForName(ie.Summary) ?? "Text clip",
            ContentType.Image => ie.Summary is { Length: > 0 } s ? $"Image {s}" : "Image",
            ContentType.Files => ie.Summary is { Length: > 0 } s ? s : "File drop",
            _ => "Clip",
        };

        static string? TruncateForName(string? summary)
        {
            if (string.IsNullOrWhiteSpace(summary))
                return null;
            string firstLine = summary.Split('\n', 2)[0].Trim();
            if (firstLine.Length > 40)
                return string.Concat(firstLine.AsSpan(0, 40), "\u2026");
            return firstLine.Length > 0 ? firstLine : null;
        }
    }

    internal static List<string> ParseDropFileNames(byte[] data)
    {
        var names = new List<string>();
        if (data.Length < 20)
            return names;

        int fileOffset = BitConverter.ToInt32(data, 0);
        bool isWide = BitConverter.ToInt32(data, 16) != 0;

        if (fileOffset < 0 || fileOffset >= data.Length)
            return names;

        if (isWide)
        {
            int pos = fileOffset;
            while (pos + 1 < data.Length)
            {
                if (data[pos] == 0 && data[pos + 1] == 0)
                    break;

                int start = pos;
                while (pos + 1 < data.Length && !(data[pos] == 0 && data[pos + 1] == 0))
                    pos += 2;

                string path = Encoding.Unicode.GetString(data, start, pos - start);
                names.Add(path);
                pos += 2;
            }
        }
        else
        {
            int pos = fileOffset;
            while (pos < data.Length)
            {
                if (data[pos] == 0)
                    break;

                int start = pos;
                while (pos < data.Length && data[pos] != 0)
                    pos++;

                string path = Encoding.Default.GetString(data, start, pos - start);
                names.Add(path);
                pos++;
            }
        }

        return names;
    }

    internal static string FormatFileNames(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
            return "0 files";

        string firstName = Path.GetFileName(paths[0]);
        if (string.IsNullOrEmpty(firstName))
            firstName = paths[0];

        if (paths.Count == 1)
            return firstName;

        return $"{firstName} +{paths.Count - 1} more";
    }

    internal static string ExtractImageDimensions(byte[] dibData)
    {
        if (dibData.Length < 16)
            return string.Empty;

        int headerSize = BitConverter.ToInt32(dibData, 0);
        if (headerSize < 16)
            return string.Empty;

        int width = BitConverter.ToInt32(dibData, 4);
        int height = BitConverter.ToInt32(dibData, 8);

        if (height < 0)
            height = -height;

        if (width <= 0 || height <= 0 || width > 100_000 || height > 100_000)
            return string.Empty;

        return $"{width}\u00D7{height}";
    }

    internal static string DecodeUtf16Truncated(byte[] data, int maxChars)
    {
        int charCount = 0;
        int byteEnd = 0;

        for (int i = 0; i + 1 < data.Length; i += 2)
        {
            if (data[i] == 0 && data[i + 1] == 0)
                break;

            charCount++;
            byteEnd = i + 2;

            if (charCount >= maxChars)
                break;
        }

        if (byteEnd == 0)
            return string.Empty;

        return Encoding.Unicode.GetString(data, 0, byteEnd);
    }

    private void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(_historyDir);
        Directory.CreateDirectory(_blobsDir);
    }

    private string GetBlobPath(string id) => Path.Combine(_blobsDir, id + ".bin");

    private void DeleteBlobQuietly(string id)
    {
        try
        {
            string path = GetBlobPath(id);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string GetDefaultHistoryDirectory()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Clipt", "History");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _gate.Dispose();
    }

    private sealed class IndexFile
    {
        public List<IndexEntry> Entries { get; set; } = [];
    }

    private sealed class IndexEntry
    {
        public string Id { get; set; } = string.Empty;
        public string? Name { get; set; }
        public DateTime TimestampUtc { get; set; }
        public uint SequenceNumber { get; set; }
        public string? OwnerProcess { get; set; }
        public int OwnerPid { get; set; }
        public string? Summary { get; set; }
        public ContentType ContentType { get; set; }
        public long DataSizeBytes { get; set; }
        public string? ContentHash { get; set; }
    }
}
