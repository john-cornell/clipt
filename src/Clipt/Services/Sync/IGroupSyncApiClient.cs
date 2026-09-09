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
