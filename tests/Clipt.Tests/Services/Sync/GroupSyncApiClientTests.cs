using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using Clipt.Services.Sync;

namespace Clipt.Tests.Services.Sync;

public class GroupSyncApiClientTests
{
    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }
        public required Func<HttpRequestMessage, HttpResponseMessage> Respond { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return Respond(request);
        }
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, object body) => new(status)
    {
        Content = JsonContent.Create(body),
    };

    [Fact]
    public void Configure_SetsBaseAddressAndAuthorizationHeader()
    {
        var handler = new StubHttpMessageHandler { Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) };
        var httpClient = new HttpClient(handler);
        var client = new GroupSyncApiClient(httpClient);

        client.Configure(new Uri("https://sync.example.com/"), "my-token");

        Assert.Equal(new Uri("https://sync.example.com/"), httpClient.BaseAddress);
        Assert.Equal("Bearer", httpClient.DefaultRequestHeaders.Authorization!.Scheme);
        Assert.Equal("my-token", httpClient.DefaultRequestHeaders.Authorization!.Parameter);
    }

    [Fact]
    public async Task GetKdfAsync_ParsesResponse()
    {
        string saltBase64 = Convert.ToBase64String([1, 2, 3, 4]);
        var handler = new StubHttpMessageHandler
        {
            Respond = _ => JsonResponse(HttpStatusCode.OK, new
            {
                salt = saltBase64,
                argon2_time_cost = 3,
                argon2_memory_cost_kib = 65536,
                argon2_parallelism = 4,
            }),
        };
        var client = new GroupSyncApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://sync.example.com/") });

        GroupSyncKdfParams result = await client.GetKdfAsync();

        Assert.Equal([1, 2, 3, 4], result.Salt);
        Assert.Equal(3, result.TimeCost);
        Assert.Equal(65536, result.MemoryCostKib);
        Assert.Equal(4, result.Parallelism);
    }

    [Fact]
    public async Task PullGroupsAsync_ParsesGroupsAndTombstones()
    {
        string ciphertextBase64 = Convert.ToBase64String([9, 9, 9]);
        var handler = new StubHttpMessageHandler
        {
            Respond = _ => JsonResponse(HttpStatusCode.OK, new
            {
                groups = new object[]
                {
                    new { id = "g1", ciphertext = ciphertextBase64, version = 5, updated_at = "2026-01-01T00:00:00Z", deleted = false },
                    new { id = "g2", ciphertext = (string?)null, version = 6, updated_at = "2026-01-01T00:00:00Z", deleted = true },
                },
                latest_version = 6,
            }),
        };
        var client = new GroupSyncApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://sync.example.com/") });

        GroupSyncPullResult result = await client.PullGroupsAsync(since: 0);

        Assert.Equal(6, result.LatestVersion);
        Assert.Equal(2, result.Groups.Count);
        Assert.Equal([9, 9, 9], result.Groups[0].Ciphertext);
        Assert.False(result.Groups[0].Deleted);
        Assert.Null(result.Groups[1].Ciphertext);
        Assert.True(result.Groups[1].Deleted);
    }

    [Fact]
    public async Task PushGroupAsync_Success_ReturnsNewVersion()
    {
        var handler = new StubHttpMessageHandler
        {
            Respond = _ => JsonResponse(HttpStatusCode.OK, new { id = "g1", version = 7 }),
        };
        var client = new GroupSyncApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://sync.example.com/") });

        int version = await client.PushGroupAsync("g1", [1, 2, 3], basedOnVersion: 6);

        Assert.Equal(7, version);
    }

    [Fact]
    public async Task PushGroupAsync_Conflict_ThrowsGroupSyncConflictException()
    {
        var handler = new StubHttpMessageHandler { Respond = _ => new HttpResponseMessage(HttpStatusCode.Conflict) };
        var client = new GroupSyncApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://sync.example.com/") });

        await Assert.ThrowsAsync<GroupSyncConflictException>(() => client.PushGroupAsync("g1", [1], basedOnVersion: 0));
    }

    [Fact]
    public async Task DeleteGroupAsync_Conflict_ThrowsGroupSyncConflictException()
    {
        var handler = new StubHttpMessageHandler { Respond = _ => new HttpResponseMessage(HttpStatusCode.Conflict) };
        var client = new GroupSyncApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://sync.example.com/") });

        await Assert.ThrowsAsync<GroupSyncConflictException>(() => client.DeleteGroupAsync("g1", basedOnVersion: 3));
    }
}
