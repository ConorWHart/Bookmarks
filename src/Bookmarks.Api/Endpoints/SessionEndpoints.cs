using System.Security.Claims;
using Bookmarks.Api.Auth;
using Bookmarks.Shared.Api;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bookmarks.Api.Endpoints;

/// <summary>Device sessions: stay signed in for sync without Entra's 24-hour limit (see <c>claude/device-sessions-plan.md</c>).</summary>
public static class SessionEndpoints
{
    public static RouteGroupBuilder MapSessionEndpoints(this RouteGroupBuilder api)
    {
        var sessions = api.MapGroup("/sessions");
        sessions.MapPost("", CreateAsync).WithName("CreateSession");
        sessions.MapPost("/rotate", RotateAsync).WithName("RotateSession");
        sessions.MapDelete("/current", RevokeCurrentAsync).WithName("RevokeCurrentSession");
        return api;
    }

    /// <summary>
    /// Trades the caller's Entra access token for a device session. Only an Entra token can do this:
    /// a session can't extend itself by creating new ones.
    /// </summary>
    internal static async Task<Results<Ok<SessionResponse>, ProblemHttpResult>> CreateAsync(
        CreateSessionRequest? request,
        ClaimsPrincipal principal,
        ICurrentUser user,
        DeviceSessionService sessions,
        CancellationToken ct)
    {
        if (IsDeviceSession(principal))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Sign in to start a session",
                detail: "A new device session needs a fresh Entra sign-in, not an existing session.");
        }

        return TypedResults.Ok(await sessions.CreateAsync(user.RequiredId(), user.DisplayName, request?.DeviceName, ct));
    }

    /// <summary>Swaps the session's token for a new one. 409 if the token was already rotated (re-read the stored token).</summary>
    internal static async Task<Results<Ok<SessionResponse>, ProblemHttpResult>> RotateAsync(
        HttpRequest http,
        ClaimsPrincipal principal,
        DeviceSessionService sessions,
        CancellationToken ct)
    {
        if (SessionId(principal) is not { } sessionId || DeviceSessionTokens.FromHeader(http) is not { } token)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, detail: "Only a device session can be rotated.");
        }

        var rotated = principal.HasClaim(c => c.Type == AuthConstants.PreviousSessionTokenClaim)
            ? null
            : await sessions.RotateAsync(sessionId, token, ct);

        return rotated is not null
            ? TypedResults.Ok(rotated)
            : TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                detail: "This token was already replaced. Use the newest token stored on the device.");
    }

    /// <summary>Sign-out: the session's tokens stop working straight away.</summary>
    internal static async Task<Results<NoContent, ProblemHttpResult>> RevokeCurrentAsync(
        ClaimsPrincipal principal,
        DeviceSessionService sessions,
        CancellationToken ct)
    {
        if (SessionId(principal) is not { } sessionId)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, detail: "The request isn't using a device session.");
        }

        await sessions.RevokeAsync(sessionId, ct);
        return TypedResults.NoContent();
    }

    private static bool IsDeviceSession(ClaimsPrincipal principal) => SessionId(principal) is not null;

    private static Guid? SessionId(ClaimsPrincipal principal)
        => Guid.TryParse(principal.FindFirst(AuthConstants.SessionIdClaim)?.Value, out var id) ? id : null;
}
