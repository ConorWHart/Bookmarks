using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;

namespace Bookmarks.UI.Services;

/// <summary>
/// Lets the Back button (Android's back button or gesture, the browser's Back) close an open drawer instead of leaving
/// the page. Each overlay that opens adds a history entry for the same page with a <c>#overlay-…</c> fragment; Back
/// returns to the entry below, which closes the overlay on top. Closing it any other way (✕, backdrop) goes back past
/// its entry, so the history stays as it was. Used through the <c>CloseOnBack</c> component.
/// </summary>
/// <remarks>
/// iOS has no Back button; Safari's swipe-back gesture behaves like Back here, and the installed app has no gesture.
/// The history entry is pushed through <see cref="NavigationManager"/>, so Blazor's own history state stays intact.
/// No LocationChanging handler on purpose: registering one makes Blazor replay every Back (undo, ask, redo), which
/// races with the history moves here. Following a link while an overlay is open (e.g. the phone menu) leaves that
/// overlay's entry behind the new page; when Back later lands on such an entry it steps back once more to the plain
/// entry of the same page, so it never costs an extra press.
/// </remarks>
public sealed class OverlayHistory : IDisposable, IAsyncDisposable
{
    private const string Prefix = "overlay-";

    private readonly NavigationManager _navigation;
    private readonly IJSRuntime _js;
    private readonly List<Entry> _stack = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IJSObjectReference? _module;

    public OverlayHistory(NavigationManager navigation, IJSRuntime js)
    {
        _navigation = navigation;
        _js = js;
        _navigation.LocationChanged += OnLocationChanged;
    }

    /// <summary>An overlay opened: Back should call <paramref name="close"/>.</summary>
    public async Task OpenedAsync(string key, Func<Task> close)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(close);

        await _gate.WaitAsync();
        try
        {
            if (_stack.Any(e => e.Key == key))
            {
                return;
            }

            _stack.Add(new Entry(key, close));
            _navigation.NavigateTo(WithoutFragment(_navigation.Uri) + "#" + Prefix + key);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The overlay closed some other way (✕, backdrop, an action): drop its history entry.</summary>
    public async Task ClosedAsync(string key)
    {
        await _gate.WaitAsync();
        try
        {
            var index = _stack.FindLastIndex(e => e.Key == key);
            if (index < 0)
            {
                return; // already closed by Back
            }

            _stack.RemoveAt(index);
            if (Fragment(_navigation.Uri) == Prefix + key)
            {
                // Waits for the history move, so an overlay opened right after doesn't get its entry popped.
                await GoBackAsync(1);
            }
        }
        catch (JSException)
        {
            // The extra history entry stays; Back then just returns to this page.
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task GoBackAsync(int entries)
    {
        _module ??= await _js.InvokeAsync<IJSObjectReference>("import", "./_content/Bookmarks.UI/js/overlayHistory.js");
        await _module.InvokeVoidAsync("back", entries);
    }

    /// <summary>Back (or any navigation): close every overlay above the entry we're now on, top first.</summary>
    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        var fragment = Fragment(e.Location);
        while (_stack.Count > 0 && Prefix + _stack[^1].Key != fragment)
        {
            var top = _stack[^1];
            _stack.RemoveAt(_stack.Count - 1);
            _ = CloseAsync(top);
        }

        // An overlay entry left behind by following a link with an overlay open: skip over it.
        if (_stack.Count == 0 && fragment.StartsWith(Prefix, StringComparison.Ordinal))
        {
            _ = SkipStaleEntryAsync();
        }
    }

    private async Task SkipStaleEntryAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_stack.Count == 0 && Fragment(_navigation.Uri).StartsWith(Prefix, StringComparison.Ordinal))
            {
                await GoBackAsync(1);
            }
        }
        catch (JSException)
        {
            // Harmless: Back just shows this page once more.
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task CloseAsync(Entry entry)
    {
        try
        {
            await entry.Close();
        }
        catch (ObjectDisposedException)
        {
            // The page that owned it has gone.
        }
    }

    private static string WithoutFragment(string uri)
    {
        var hash = uri.IndexOf('#', StringComparison.Ordinal);
        return hash < 0 ? uri : uri[..hash];
    }

    private static string Fragment(string uri)
    {
        var hash = uri.IndexOf('#', StringComparison.Ordinal);
        return hash < 0 ? "" : uri[(hash + 1)..];
    }

    public void Dispose()
    {
        _navigation.LocationChanged -= OnLocationChanged;
        _gate.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The page is going away.
            }
        }

        Dispose();
    }

    private sealed record Entry(string Key, Func<Task> Close);
}
