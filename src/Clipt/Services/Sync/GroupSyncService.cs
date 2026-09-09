using System.Net.Http;

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
