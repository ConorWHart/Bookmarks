using System.Text.Json;
using Bookmarks.Core.Storage;
using Bookmarks.Core.Sync;
using Bookmarks.Shared.Sync;

namespace Bookmarks.Tests.Sync;

/// <summary>
/// An in-memory stand-in for /api/sync with the same rules as <c>SyncService</c> (one user): insert, accept when the
/// base version matches, otherwise last writer wins; pull by version, oldest first. Payloads cross as JSON, so each
/// device gets its own copies.
/// </summary>
internal sealed class FakeSyncServer : ISyncTransport
{
    private readonly Dictionary<(string Type, Guid Id), Row> _rows = [];
    private long _lastVersion = 1000;
    private int _pullCalls;

    /// <summary>Every call throws, like a device with no connection.</summary>
    public bool Offline { get; set; }

    /// <summary>Throws on the n-th pull call from now (1-based, across devices), once.</summary>
    public int? FailPullCall
    {
        get => _failPullCall;
        set
        {
            _failPullCall = value;
            _pullCalls = 0;
        }
    }

    private int? _failPullCall;

    /// <summary>Runs while the server handles a push, after storing and before answering.</summary>
    public Func<Task>? DuringPush { get; set; }

    /// <summary>Runs before the server builds a pull page.</summary>
    public Func<Task>? BeforePull { get; set; }

    public List<long> PullSinceValues { get; } = [];

    public int RecordCount => _rows.Count;

    public async Task<SyncPushResponse> PushAsync(SyncPushRequest request, CancellationToken ct = default)
    {
        ThrowIfOffline();
        var results = new List<SyncPushResult>();

        foreach (var change in request.Changes)
        {
            var key = (change.EntityType, change.Id);
            var wins = !_rows.TryGetValue(key, out var row)
                || row.Version == change.BaseVersion
                || change.UpdatedAt > row.UpdatedAt;

            if (wins)
            {
                var version = ++_lastVersion;
                _rows[key] = new Row(change.EntityType, change.Id, version, change.UpdatedAt, change.IsDeleted, change.Payload.GetRawText());
                results.Add(new SyncPushResult(change.EntityType, change.Id, true, version));
            }
            else
            {
                results.Add(new SyncPushResult(change.EntityType, change.Id, false, row!.Version));
            }
        }

        if (DuringPush is { } hook)
        {
            await hook();
        }

        return new SyncPushResponse(results);
    }

    public async Task<SyncPullResponse> PullAsync(long since, int limit, CancellationToken ct = default)
    {
        ThrowIfOffline();
        _pullCalls++;
        PullSinceValues.Add(since);

        if (_failPullCall == _pullCalls)
        {
            _failPullCall = null;
            throw new HttpRequestException("Connection lost during pull.");
        }

        if (BeforePull is { } hook)
        {
            await hook();
        }

        var page = _rows.Values
            .Where(r => r.Version > since)
            .OrderBy(r => r.Version)
            .Take(limit + 1)
            .ToList();
        var hasMore = page.Count > limit;
        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        var changes = page
            .Select(r =>
            {
                using var doc = JsonDocument.Parse(r.Json);
                return new SyncChange(r.Type, r.Id, r.Version, r.UpdatedAt, r.IsDeleted, doc.RootElement.Clone());
            })
            .ToList();

        return new SyncPullResponse(changes, page.Count > 0 ? page[^1].Version : since, hasMore);
    }

    /// <summary>The server's copy of a record.</summary>
    public T Get<T>(Guid id)
        where T : SyncEntity
    {
        var type = SyncEntityTypes.Of<T>();
        var row = _rows[(type.Name, id)];
        using var doc = JsonDocument.Parse(row.Json);
        var entity = (T)type.FromPayload(doc.RootElement);
        entity.IsDeleted = row.IsDeleted;
        entity.UpdatedAt = row.UpdatedAt;
        entity.Version = row.Version;
        return entity;
    }

    private void ThrowIfOffline()
    {
        if (Offline)
        {
            throw new HttpRequestException("No connection.");
        }
    }

    private sealed record Row(string Type, Guid Id, long Version, DateTimeOffset UpdatedAt, bool IsDeleted, string Json);
}

/// <summary>One installation: its own store and engine, talking to a shared <see cref="FakeSyncServer"/>.</summary>
internal sealed class Device(FakeSyncServer server, int pullPageSize = SyncLimits.DefaultPullLimit)
{
    public InMemoryLocalStore Store { get; } = new();

    public SyncEngine Engine => _engine ??= new SyncEngine(Store, server, TimeProvider.System) { PullPageSize = pullPageSize };

    private SyncEngine? _engine;

    public Task<SyncResult> SyncAsync() => Engine.SyncAsync();

    public async Task<T> AddAsync<T>(T entity)
        where T : SyncEntity
    {
        await Store.UpsertAsync(entity);
        return entity;
    }

    /// <summary>A local edit, like LibraryService makes: read, change, touch, save. <paramref name="at"/> pins the timestamp.</summary>
    public async Task EditAsync<T>(Guid id, Action<T> change, DateTimeOffset? at = null)
        where T : SyncEntity
    {
        var entity = await Store.GetAsync<T>(id) ?? throw new InvalidOperationException($"{typeof(T).Name} {id} not found.");
        change(entity);
        entity.Touch();
        if (at is { } t)
        {
            entity.UpdatedAt = t;
        }

        await Store.UpsertAsync(entity);
    }

    public async Task<T> GetAsync<T>(Guid id)
        where T : SyncEntity
        => await Store.GetAsync<T>(id) ?? throw new InvalidOperationException($"{typeof(T).Name} {id} not found.");
}
