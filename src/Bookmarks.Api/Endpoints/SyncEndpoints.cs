using System.Text.Json;
using Bookmarks.Api.Sync;
using Bookmarks.Shared.Sync;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bookmarks.Api.Endpoints;

public static class SyncEndpoints
{
    public static RouteGroupBuilder MapSyncEndpoints(this RouteGroupBuilder api)
    {
        var sync = api.MapGroup("/sync");
        sync.MapPost("/push", PushAsync).WithName("SyncPush");
        sync.MapGet("/pull", PullAsync).WithName("SyncPull");
        return api;
    }

    /// <summary>Stores the device's changes. A rejected change isn't an error: the device picks up the winner on its next pull.</summary>
    internal static async Task<Results<Ok<SyncPushResponse>, ValidationProblem>> PushAsync(
        SyncPushRequest request,
        SyncService sync,
        CancellationToken ct)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        try
        {
            return TypedResults.Ok(await sync.PushAsync(request.Changes, ct));
        }
        catch (JsonException ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["changes"] = [$"A payload could not be read: {ex.Message}"],
            });
        }
    }

    /// <summary>The caller's changes after the checkpoint <paramref name="since"/> (0 = everything).</summary>
    internal static async Task<Results<Ok<SyncPullResponse>, ValidationProblem>> PullAsync(
        SyncService sync,
        CancellationToken ct,
        long since = 0,
        int? limit = null)
    {
        if (since < 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["since"] = ["The checkpoint can't be negative."],
            });
        }

        var take = Math.Clamp(limit ?? SyncLimits.DefaultPullLimit, 1, SyncLimits.MaxPullLimit);
        return TypedResults.Ok(await sync.PullAsync(since, take, ct));
    }

    private static Dictionary<string, string[]> Validate(SyncPushRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var changes = request.Changes;

        if (changes is null)
        {
            errors["changes"] = ["Changes are required."];
            return errors;
        }

        if (changes.Count > SyncLimits.MaxPushBatch)
        {
            errors["changes"] = [$"At most {SyncLimits.MaxPushBatch} changes can be pushed at once."];
            return errors;
        }

        var seen = new HashSet<(string, Guid)>();
        for (var i = 0; i < changes.Count; i++)
        {
            var change = changes[i];
            string? problem = null;

            if (change is null)
            {
                problem = "The change is empty.";
            }
            else if (!SyncService.IsSynced(change.EntityType))
            {
                problem = $"Unknown entity type '{change.EntityType}'.";
            }
            else if (change.Id == Guid.Empty)
            {
                problem = "The id is missing.";
            }
            else if (change.BaseVersion < 0)
            {
                problem = "The base version can't be negative.";
            }
            else if (change.Payload.ValueKind != JsonValueKind.Object)
            {
                problem = "The payload must be a JSON object.";
            }
            else if (!seen.Add((change.EntityType, change.Id)))
            {
                problem = "The same record appears more than once.";
            }

            if (problem is not null)
            {
                errors[$"changes[{i}]"] = [problem];
            }
        }

        return errors;
    }
}
