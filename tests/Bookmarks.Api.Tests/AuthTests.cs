using System.Net;

namespace Bookmarks.Api.Tests;

/// <summary>Pipeline and authorization rules. No database needed.</summary>
public sealed class AuthTests : IClassFixture<AuthTests.Factory>
{
    public sealed class Factory : IDisposable
    {
        public ApiFactory Api { get; } = new();

        public void Dispose() => Api.Dispose();
    }

    private readonly ApiFactory _api;

    public AuthTests(Factory factory) => _api = factory.Api;

    [Fact]
    public async Task Health_is_anonymous()
    {
        var response = await _api.CreateClientFor(null).GetAsync(new Uri("/health", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Version_is_anonymous()
    {
        var response = await _api.CreateClientFor(null).GetAsync(new Uri("/api/version", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Me_without_a_token_is_401()
    {
        var response = await _api.CreateClientFor(null).GetAsync(new Uri("/api/me", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_without_the_api_scope_is_403()
    {
        var client = _api.CreateClientFor(Guid.CreateVersion7(), scopes: "openid profile");
        var response = await client.GetAsync(new Uri("/api/me", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Me_with_a_non_guid_oid_is_403()
    {
        // The policy requires oid to be a GUID, so a malformed one never reaches an endpoint.
        var client = _api.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "not-a-guid");
        var response = await client.GetAsync(new Uri("/api/me", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
