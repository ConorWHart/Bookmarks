using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Bookmarks.Api.Auth;

public static class AuthConstants
{
    /// <summary>Every /api endpoint (except version) requires a user token with the API scope.</summary>
    public const string ApiUserPolicy = "ApiUser";

    /// <summary>Delegated scope exposed by the "Bookmarks API" app registration.</summary>
    public const string ApiScope = "access_as_user";

    public const string ObjectIdClaim = "oid";

    public const string ScopeClaim = "scp";

    public const string NameClaim = "name";
}

/// <summary>Bound from the "Auth" configuration section.</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>External ID authority, e.g. https://bookmarksapp.ciamlogin.com/{tenantId}/v2.0</summary>
    public string Authority { get; set; } = "";

    /// <summary>Application (client) id of the "Bookmarks API" app registration.</summary>
    public string Audience { get; set; } = "";
}

public static class AuthSetup
{
    public static IServiceCollection AddBookmarksAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var auth = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Metadata (issuer, signing keys) comes from {Authority}/.well-known/openid-configuration.
                options.Authority = auth.Authority;

                // Keep raw claim names ("oid", "scp", "name") instead of the legacy SOAP-style URIs.
                options.MapInboundClaims = false;

                options.TokenValidationParameters.NameClaimType = AuthConstants.NameClaim;

                // v2 access tokens carry the client id; accept the App ID URI form too in case the
                // registration issues v1-style audiences.
                options.TokenValidationParameters.ValidAudiences = string.IsNullOrEmpty(auth.Audience)
                    ? Array.Empty<string>()
                    : new[] { auth.Audience, $"api://{auth.Audience}" };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthConstants.ApiUserPolicy, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context =>
                    Guid.TryParse(context.User.FindFirst(AuthConstants.ObjectIdClaim)?.Value, out _)
                    && HasScope(context.User, AuthConstants.ApiScope)));

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        return services;
    }

    /// <summary><c>scp</c> is a single space-separated claim in Entra access tokens.</summary>
    private static bool HasScope(System.Security.Claims.ClaimsPrincipal user, string scope)
        => user.FindAll(AuthConstants.ScopeClaim)
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(scope, StringComparer.Ordinal);
}
