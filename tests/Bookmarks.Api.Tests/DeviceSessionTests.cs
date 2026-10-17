using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Bookmarks.Api.Auth;
using Bookmarks.Shared.Api;

namespace Bookmarks.Api.Tests;

/// <summary>Device sessions (/api/sessions and Bearer bms_… tokens) against a real SQL Server (skipped without Docker).</summary>
[Collection(SqlServerCollection.Name)]
public sealed class DeviceSessionTests : IDisposable
{
    private static readonly Uri SessionsUri = new("/api/sessions", UriKind.Relative);
    private static readonly Uri RotateUri = new("/api/sessions/rotate", UriKind.Relative);
    private static readonly Uri CurrentUri = new("/api/sessions/current", UriKind.Relative);
    private static readonly Uri MeUri = new("/api/me", UriKind.Relative);

    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
    private readonly ApiFactory _api;

    public DeviceSessionTests(SqlServerFixture sql) => _api = new ApiFactory(sql.ConnectionString, _time);

    public void Dispose() => _api.Dispose();

    private ApiFactory Api => _api;

    [DockerFact]
    public async Task A_session_created_with_an_entra_token_signs_in_later_calls()
    {
        var user = Guid.CreateVersion7();
        var session = await CreateSessionAsync(user, name: "Conor");

        Assert.StartsWith(SessionTokens.Prefix, session.Token, StringComparison.Ordinal);
        Assert.Equal(_time.GetUtcNow() + DeviceSessionDefaults.Lifetime, session.ExpiresAt);
        Assert.Equal(_time.GetUtcNow() + DeviceSessionDefaults.RotateAfter, session.RotateAfter);

        var me = await Bearer(session.Token).GetFromJsonAsync<MeResponse>(MeUri);
        Assert.NotNull(me);
        Assert.Equal(user, me.UserId);
        Assert.Equal("Conor", me.DisplayName);
    }

    [DockerFact]
    public async Task A_session_cannot_create_another_session()
    {
        var session = await CreateSessionAsync(Guid.CreateVersion7());

        var response = await Bearer(session.Token).PostAsJsonAsync(SessionsUri, new CreateSessionRequest("Again"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [DockerFact]
    public async Task An_unknown_token_is_401()
    {
        var response = await Bearer(DeviceSessionTokens.NewToken()).GetAsync(MeUri);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [DockerFact]
    public async Task Signing_out_ends_the_session()
    {
        var session = await CreateSessionAsync(Guid.CreateVersion7());
        var client = Bearer(session.Token);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(CurrentUri)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(MeUri)).StatusCode);
    }

    [DockerFact]
    public async Task A_session_ends_after_90_days_without_use()
    {
        var session = await CreateSessionAsync(Guid.CreateVersion7());

        _time.Advance(DeviceSessionDefaults.Lifetime + TimeSpan.FromMinutes(1));

        Assert.Equal(HttpStatusCode.Unauthorized, (await Bearer(session.Token).GetAsync(MeUri)).StatusCode);
    }

    [DockerFact]
    public async Task Using_a_session_keeps_it_alive()
    {
        var session = await CreateSessionAsync(Guid.CreateVersion7());
        var client = Bearer(session.Token);

        // 80 + 80 days: past the first 90, but never 90 days without a call.
        _time.Advance(TimeSpan.FromDays(80));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(MeUri)).StatusCode);
        _time.Advance(TimeSpan.FromDays(80));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(MeUri)).StatusCode);
    }

    [DockerFact]
    public async Task Rotation_gives_a_new_token_and_the_old_one_works_only_briefly()
    {
        var first = await CreateSessionAsync(Guid.CreateVersion7());
        _time.Advance(DeviceSessionDefaults.RotateAfter);

        var response = await Bearer(first.Token).PostAsync(RotateUri, content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var second = await response.Content.ReadFromJsonAsync<SessionResponse>();
        Assert.NotNull(second);
        Assert.NotEqual(first.Token, second.Token);

        // Both work during the grace window; rotating with the old one again is refused.
        Assert.Equal(HttpStatusCode.OK, (await Bearer(second.Token).GetAsync(MeUri)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Bearer(first.Token).GetAsync(MeUri)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Bearer(first.Token).PostAsync(RotateUri, content: null)).StatusCode);

        // After it, only the new one.
        _time.Advance(DeviceSessionDefaults.RotationGrace + TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Bearer(first.Token).GetAsync(MeUri)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Bearer(second.Token).GetAsync(MeUri)).StatusCode);
    }

    [DockerFact]
    public async Task Sessions_only_see_their_own_users_data()
    {
        var alice = await CreateSessionAsync(Guid.CreateVersion7());
        var bobId = Guid.CreateVersion7();
        var bob = await CreateSessionAsync(bobId);

        var me = await Bearer(bob.Token).GetFromJsonAsync<MeResponse>(MeUri);

        Assert.NotNull(me);
        Assert.Equal(bobId, me.UserId);
        Assert.NotEqual(alice.Token, bob.Token);
    }

    /// <summary>Signs in "with Entra" (the test auth headers) and creates a session.</summary>
    private async Task<SessionResponse> CreateSessionAsync(Guid user, string? name = null)
    {
        var response = await Api.CreateClientFor(user, name: name).PostAsJsonAsync(SessionsUri, new CreateSessionRequest("Test browser"));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SessionResponse>() ?? throw new InvalidOperationException("No session returned.");
    }

    /// <summary>A client that sends only the device session token (no test user headers).</summary>
    private HttpClient Bearer(string token)
    {
        var client = Api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
