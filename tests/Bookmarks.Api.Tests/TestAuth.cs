using System.Security.Claims;
using System.Text.Encodings.Web;
using Bookmarks.Api.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bookmarks.Api.Tests;

/// <summary>
/// Stands in for JwtBearer in tests. The caller is described by headers:
/// <c>X-Test-User</c> (oid GUID) and optional <c>X-Test-Scopes</c> (space-separated, default access_as_user).
/// No <c>X-Test-User</c> header = anonymous.
/// </summary>
internal sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string UserHeader = "X-Test-User";
    public const string ScopesHeader = "X-Test-Scopes";
    public const string NameHeader = "X-Test-Name";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var userId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var scopes = Request.Headers.TryGetValue(ScopesHeader, out var s) ? s.ToString() : AuthConstants.ApiScope;

        var claims = new List<Claim>
        {
            new(AuthConstants.ObjectIdClaim, userId.ToString()),
            new(AuthConstants.ScopeClaim, scopes),
        };

        if (Request.Headers.TryGetValue(NameHeader, out var name))
        {
            claims.Add(new Claim(AuthConstants.NameClaim, name.ToString()));
        }

        var identity = new ClaimsIdentity(claims, SchemeName, AuthConstants.NameClaim, roleType: null);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

/// <summary>A fixed user for DbContext-level tests.</summary>
internal sealed class FixedUser(Guid id) : ICurrentUser
{
    public Guid? Id => id;

    public string? DisplayName => null;
}
