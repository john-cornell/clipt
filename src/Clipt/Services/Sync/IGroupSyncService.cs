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
