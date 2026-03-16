namespace ClipSpy.Models;

public sealed record MemoryInfo(
    string HandleHex,
    string LockPointerHex,
    long AllocationSize,
    byte[] FirstBytes);
