namespace ClipSpy.Models;

public sealed record FileDropEntry(
    string FullPath,
    string FileName,
    long SizeBytes,
    string SizeFormatted,
    DateTime LastModified,
    bool IsDirectory);
