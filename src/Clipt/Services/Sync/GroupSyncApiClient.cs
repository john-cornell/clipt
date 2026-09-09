using System.Net;
using System.Net.Http;
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
