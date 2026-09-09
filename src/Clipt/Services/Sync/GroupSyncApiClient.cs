using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Clipt.Services.Sync;

public sealed class GroupSyncApiClient : IGroupSyncApiClient
{
    private readonly HttpClient _httpClient;
    private Uri? _baseAddress;
    private string? _bearerToken;

    public GroupSyncApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <summary>
    /// Safe to call repeatedly (e.g. Enable, then later Unlock, in the same running app). Never touches
    /// <see cref="HttpClient.BaseAddress"/>/<see cref="HttpClient.DefaultRequestHeaders"/> — .NET forbids
    /// changing those on an <see cref="HttpClient"/> that has already sent a request, and this client is a
    /// long-lived DI singleton that may well have already sent one by the time Configure is called again.
    /// Every request instead builds its own absolute URI against the stored base address and attaches its
    /// own per-request Authorization header.
    /// </summary>
    public void Configure(Uri baseAddress, string bearerToken)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        ArgumentException.ThrowIfNullOrEmpty(bearerToken);

        _baseAddress = baseAddress;
        _bearerToken = bearerToken;
    }

    public async Task<GroupSyncKdfParams> GetKdfAsync(CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response = await SendAsync(HttpMethod.Get, "kdf", content: null, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        KdfResponseDto dto = await response.Content.ReadFromJsonAsync<KdfResponseDto>(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Server returned an empty /kdf response.");

        return new GroupSyncKdfParams(
            Convert.FromBase64String(dto.Salt), dto.ArgonTimeCost, dto.ArgonMemoryCostKib, dto.ArgonParallelism);
    }

    public async Task<GroupSyncPullResult> PullGroupsAsync(long since, CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response = await SendAsync(
            HttpMethod.Get, $"groups?since={since}", content: null, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        PullResponseDto dto = await response.Content.ReadFromJsonAsync<PullResponseDto>(cancellationToken).ConfigureAwait(false)
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
        HttpResponseMessage response = await SendAsync(
            HttpMethod.Put, $"groups/{Uri.EscapeDataString(groupId)}", JsonContent.Create(body), cancellationToken).ConfigureAwait(false);

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

        HttpResponseMessage response = await SendAsync(
            HttpMethod.Delete,
            $"groups/{Uri.EscapeDataString(groupId)}",
            JsonContent.Create(new DeleteRequestDto(basedOnVersion)),
            cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Conflict)
            throw new GroupSyncConflictException();
        response.EnsureSuccessStatusCode();

        UpsertResponseDto dto = await response.Content.ReadFromJsonAsync<UpsertResponseDto>(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Server returned an empty delete response.");
        return dto.Version;
    }

    private Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string relativePath, HttpContent? content, CancellationToken cancellationToken)
    {
        if (_baseAddress is null || _bearerToken is null)
            throw new InvalidOperationException("Call Configure(...) before making requests.");

        var request = new HttpRequestMessage(method, new Uri(_baseAddress, relativePath))
        {
            Content = content,
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", _bearerToken) },
        };
        return _httpClient.SendAsync(request, cancellationToken);
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
