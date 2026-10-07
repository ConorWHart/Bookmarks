using System.Security.Claims;

namespace Bookmarks.Api.Auth;

/// <summary>The signed-in user of the current request, taken from the access token only.</summary>
public interface ICurrentUser
{
    /// <summary>The Entra object id (<c>oid</c>) of the user; null when the request isn't authenticated.</summary>
    Guid? Id { get; }

    string? DisplayName { get; }
}

public static class CurrentUserExtensions
{
    /// <summary>The user id, for endpoints behind the <see cref="AuthConstants.ApiUserPolicy"/> policy (which guarantees it).</summary>
    public static Guid RequiredId(this ICurrentUser user)
        => user.Id ?? throw new InvalidOperationException("The request has no authenticated user.");
}

/// <summary>Reads the user from <see cref="HttpContext.User"/>. Claims keep their raw JWT names (MapInboundClaims = false).</summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? Id => Guid.TryParse(Principal?.FindFirst(AuthConstants.ObjectIdClaim)?.Value, out var id) ? id : null;

    public string? DisplayName => Principal?.FindFirst(AuthConstants.NameClaim)?.Value;
}

/// <summary>No user: used at design time (migrations). Query filters then match no rows.</summary>
public sealed class NoCurrentUser : ICurrentUser
{
    public static readonly NoCurrentUser Instance = new();

    public Guid? Id => null;

    public string? DisplayName => null;
}
