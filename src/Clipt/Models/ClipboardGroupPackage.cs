namespace Clipt.Models;

/// <summary>Portable saved-group file (.cliptgroup): manifest + blobs/*.bin</summary>
public static class ClipboardGroupPackage
{
    public const string FileExtension = ".cliptgroup";
    public const int FormatVersion = 1;
    public const string ManifestEntryName = "manifest.json";
    public const string BlobsPrefix = "blobs/";
}

public sealed class ClipboardGroupPackageManifest
{
    public int FormatVersion { get; set; }
    public DateTime ExportedUtc { get; set; }
    public string ExporterAppVersion { get; set; } = string.Empty;
    public ClipboardGroupPackageGroupPayload Group { get; set; } = null!;
}

public sealed class ClipboardGroupPackageGroupPayload
{
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public List<ClipboardGroupPackageArchivedEntry> ArchivedEntries { get; set; } = [];
}

public sealed class ClipboardGroupPackageArchivedEntry
{
    public string Id { get; set; } = string.Empty;
    public string SourceEntryId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; }
    public uint SequenceNumber { get; set; }
    public string OwnerProcess { get; set; } = string.Empty;
    public int OwnerPid { get; set; }
    public string Summary { get; set; } = string.Empty;
    public ContentType ContentType { get; set; }
    public long DataSizeBytes { get; set; }
    public string ContentHash { get; set; } = string.Empty;
}

public readonly record struct GroupPackageOperationResult(bool Success, string? ErrorMessage = null)
{
    public static GroupPackageOperationResult Ok() => new(true);

    public static GroupPackageOperationResult Fail(string message) => new(false, message);
}
