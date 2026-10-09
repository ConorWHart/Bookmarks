using System.Net;
using System.Runtime.ExceptionServices;
using Bookmarks.Client.Api;
using Bookmarks.Client.Storage;
using Bookmarks.Core.Sync;
using Bookmarks.UI.Services;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Bookmarks.Client.Sync;

/// <summary>
/// Decides when <see cref="SyncEngine"/> runs in the PWA, and reports its status to the UI.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Triggers: startup, a few seconds after each local edit, coming back online, the tab becoming visible again,
/// every few minutes while open, and Sync now.</item>
/// <item>Never blocks the UI: passes run in the background and edits keep working (they queue in the outbox).</item>
/// <item>One pass at a time across tabs (Web Locks, <c>js/sync.js</c>); after a pull that changed the library the other
/// tabs are told to reload.</item>
/// <item>Failures back off (15 s doubling to 5 min): the serverless database can take up to a minute to wake.
/// Only repeated failures, or a sign-in that needs renewing, show as "needs attention".</item>
/// <item>Guests never sync.</item>
/// </list>
/// </remarks>
public sealed class BrowserSyncService(
    IJSRuntime js,
    IndexedDbLocalStore store,
    SyncEngine engine,
    IAccountService accounts,
    IAppStatusService appStatus,
    LibraryService library,
    ILogger<BrowserSyncService> logger)
    : ISyncService, IAsyncDisposable
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan AfterEditDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan OtherTabBusyDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PassTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(1);

    /// <summary>Consecutive failures before a network-type problem is shown.</summary>
    private const int FailuresBeforeAttention = 2;

    private Timer? _timer;
    private DateTimeOffset _dueAt = DateTimeOffset.MaxValue;
    private IJSObjectReference? _module;
    private DotNetObjectReference<BrowserSyncService>? _self;
    private bool _started;
    private bool _running;
    private bool _runAgain;
    private bool _disposed;
    private int _failures;
    private bool _authProblem;
    private string? _lastError;
    private Exception? _passError;
    private TimeSpan _retryDelay = FirstRetryDelay;

    public bool IsEnabled => accounts.IsSignInSupported && !accounts.Current.IsGuest;

    public SyncStatus Status
    {
        get
        {
            if (!IsEnabled)
            {
                return SyncStatus.Off;
            }

            if (_running)
            {
                return SyncStatus.Syncing;
            }

            if (!appStatus.IsOnline)
            {
                return SyncStatus.Offline;
            }

            if (Problem is not null)
            {
                return SyncStatus.NeedsAttention;
            }

            return PendingChanges > 0 || LastSyncedAt is null ? SyncStatus.Pending : SyncStatus.Synced;
        }
    }

    public DateTimeOffset? LastSyncedAt { get; private set; }

    public int PendingChanges { get; private set; }

    public string? Problem => _lastError is not null && (_authProblem || _failures >= FailuresBeforeAttention)
        ? _lastError
        : null;

    public event Action? Changed;

    public async Task StartAsync()
    {
        if (_started || !IsEnabled)
        {
            return;
        }

        _started = true;
        _timer = new Timer(_ => _ = RunScheduledAsync(), null, Timeout.Infinite, Timeout.Infinite);

        try
        {
            LastSyncedAt = await engine.GetLastSyncedAtAsync();
            PendingChanges = await store.CountOutboxAsync();
        }
        catch (JSException ex)
        {
            logger.LogWarning(ex, "Couldn't read the sync state.");
        }

        store.LocalChanged += OnLocalChanged;
        appStatus.Changed += OnAppStatusChanged;

        try
        {
            _module = await js.InvokeAsync<IJSObjectReference>("import", "./js/sync.js");
            _self = DotNetObjectReference.Create(this);
            await _module.InvokeVoidAsync("start", _self, store.DatabaseName);
        }
        catch (JSException ex)
        {
            // Without the module a pass still runs, just without cross-tab coordination.
            logger.LogWarning(ex, "Sync tab coordination is unavailable.");
            _module = null;
        }

        Schedule(StartupDelay);
        Changed?.Invoke();
    }

    public async Task<bool> SyncNowAsync()
    {
        if (!IsEnabled)
        {
            return false;
        }

        if (!_started)
        {
            await StartAsync();
        }

        // Let a pass already running in this tab finish, then run a fresh one so it includes the latest edits.
        while (_running)
        {
            await Task.Delay(200);
        }

        return await TrySyncAsync(waitForOtherTabs: true);
    }

    // ---------- Called from js/sync.js ----------

    /// <summary>One engine pass, run while holding the cross-tab lock. Returns true when the library changed.</summary>
    [JSInvokable]
    public async Task<bool> RunPassAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(PassTimeout);
            var result = await engine.SyncAsync(timeout.Token);
            LastSyncedAt = await engine.GetLastSyncedAtAsync();

            if (result.LocalDataChanged)
            {
                await library.ReloadAsync();
            }

            logger.LogDebug(
                "Sync pass: {Pushed} pushed, {Rejected} superseded, {Pulled} pulled.", result.Pushed, result.Rejected, result.Pulled);
            return result.LocalDataChanged;
        }
        catch (Exception ex)
        {
            // Rethrown by TrySyncAsync once the lock is released; exceptions don't survive the JS round trip intact.
            _passError = ex;
            return false;
        }
    }

    [JSInvokable]
    public async Task OnOtherTabSynced()
    {
        try
        {
            await library.ReloadAsync();
            LastSyncedAt = await engine.GetLastSyncedAtAsync();
            PendingChanges = await store.CountOutboxAsync();
            Changed?.Invoke();
        }
        catch (JSException ex)
        {
            logger.LogWarning(ex, "Couldn't reload after another tab synced.");
        }
    }

    [JSInvokable]
    public void OnBecameVisible()
    {
        if (LastSyncedAt is null || DateTimeOffset.UtcNow - LastSyncedAt > StaleAfter)
        {
            Schedule(StartupDelay);
        }
    }

    // ---------- Scheduling ----------

    private void OnLocalChanged()
    {
        PendingChanges++; // Exact count is refreshed after the next pass.
        Schedule(AfterEditDelay);
        Changed?.Invoke();
    }

    private void OnAppStatusChanged()
    {
        if (appStatus.IsOnline)
        {
            Schedule(StartupDelay);
        }

        Changed?.Invoke();
    }

    /// <summary>Runs a pass after <paramref name="delay"/>, unless one is already due sooner.</summary>
    private void Schedule(TimeSpan delay)
    {
        if (_timer is null || _disposed)
        {
            return;
        }

        var due = DateTimeOffset.UtcNow + delay;
        if (due >= _dueAt)
        {
            return;
        }

        _dueAt = due;
        _timer.Change(delay, Timeout.InfiniteTimeSpan);
    }

    private async Task RunScheduledAsync()
    {
        _dueAt = DateTimeOffset.MaxValue;
        try
        {
            await TrySyncAsync(waitForOtherTabs: false);
        }
        catch (Exception ex)
        {
            // A timer callback must never throw.
            logger.LogError(ex, "Scheduled sync failed unexpectedly.");
        }
    }

    private async Task<bool> TrySyncAsync(bool waitForOtherTabs)
    {
        if (!IsEnabled || _disposed)
        {
            return false;
        }

        if (!appStatus.IsOnline)
        {
            Changed?.Invoke(); // Coming back online schedules a pass.
            return false;
        }

        if (_running)
        {
            _runAgain = true;
            return false;
        }

        _running = true;
        _passError = null;
        Changed?.Invoke();

        var completed = false;
        try
        {
            var ran = _module is not null
                ? await _module.InvokeAsync<bool>("runExclusive", _self, waitForOtherTabs)
                : await RunWithoutLockAsync();

            if (_passError is { } error)
            {
                ExceptionDispatchInfo.Capture(error).Throw();
            }

            if (!ran)
            {
                Schedule(OtherTabBusyDelay); // Another tab is syncing this account right now.
                return false;
            }

            _failures = 0;
            _authProblem = false;
            _lastError = null;
            _retryDelay = FirstRetryDelay;
            completed = true;
            Schedule(Interval);
            return true;
        }
        catch (Exception ex)
        {
            _failures++;
            (_lastError, _authProblem) = Describe(ex);
            logger.LogWarning(ex, "Sync failed ({Failures} in a row); retrying in {Delay}.", _failures, _retryDelay);
            Schedule(_retryDelay);
            _retryDelay = TimeSpan.FromTicks(Math.Min(_retryDelay.Ticks * 2, MaxRetryDelay.Ticks));
            return false;
        }
        finally
        {
            _running = false;
            try
            {
                PendingChanges = await store.CountOutboxAsync();
            }
            catch (JSException)
            {
                // Keep the previous count.
            }

            if (_runAgain)
            {
                _runAgain = false;
                Schedule(completed ? AfterEditDelay : _retryDelay);
            }

            Changed?.Invoke();
        }
    }

    private async Task<bool> RunWithoutLockAsync()
    {
        await RunPassAsync();
        return true;
    }

    private static (string Message, bool IsAuth) Describe(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: HttpStatusCode.Unauthorized } =>
            ("Your sign-in needs renewing. Sign out and back in to keep syncing. Your changes are safe on this device.", true),
        HttpRequestException { StatusCode: HttpStatusCode.Forbidden } =>
            ("Your sign-in is missing permission to sync. Sign out and back in.", true),
        SignInRequiredException =>
            ("Couldn't renew your sign-in. If you're online, sign out and back in to keep syncing. Your changes are safe on this device.", false),
        _ => ("Couldn't reach the Bookmarks service. Your changes are safe on this device and will sync when it's back.", false),
    };

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        store.LocalChanged -= OnLocalChanged;
        appStatus.Changed -= OnAppStatusChanged;

        if (_timer is not null)
        {
            await _timer.DisposeAsync();
        }

        if (_module is not null)
        {
            try
            {
                await _module.InvokeVoidAsync("stop");
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The page is going away.
            }
        }

        _self?.Dispose();
    }
}
