namespace Bookmarks.UI.Services;

/// <summary>The theme choice on the Settings page.</summary>
public enum ThemeMode
{
    /// <summary>Follows the device's light/dark setting, live.</summary>
    System,
    Light,
    Dark,
}

/// <summary>
/// Light / dark / match-system theme, shared by <c>MainLayout</c> (which owns the <c>MudThemeProvider</c>) and the
/// Settings page. Saved per device (and per account database) in <see cref="UserPreferences.Theme"/>.
/// </summary>
/// <remarks>
/// The layout calls <see cref="InitializeAsync"/> once after its first render, passing a way to read the system
/// setting, and forwards the provider's system-change notifications to <see cref="OnSystemDarkModeChangedAsync"/>.
/// </remarks>
public sealed class ThemeService(UserPreferences prefs)
{
    private const string LightValue = "light";
    private const string DarkValue = "dark";
    private const string SystemValue = "system";

    private Func<Task<bool>>? _readSystemDarkMode;
    private bool _systemDark;

    public ThemeMode Mode { get; private set; } = ThemeMode.System;

    /// <summary>What the app shows right now (the system's setting when <see cref="Mode"/> is System).</summary>
    public bool IsDarkMode { get; private set; }

    public bool IsInitialized { get; private set; }

    public event Action? Changed;

    /// <summary>Restores the saved choice (migrating the old dark-mode toggle) and reads the system setting.</summary>
    public async Task InitializeAsync(Func<Task<bool>> readSystemDarkMode)
    {
        ArgumentNullException.ThrowIfNull(readSystemDarkMode);
        _readSystemDarkMode = readSystemDarkMode;

        Mode = await LoadModeAsync();
        _systemDark = await TryReadSystemAsync();
        IsInitialized = true;
        Apply();
    }

    public async Task SetModeAsync(ThemeMode mode)
    {
        Mode = mode;
        if (mode == ThemeMode.System)
        {
            // The provider only watches the system while the mode is System, so refresh what we know.
            _systemDark = await TryReadSystemAsync();
        }

        Apply();

        try
        {
            await prefs.SetStringAsync(UserPreferences.Theme, ToValue(mode));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Not remembered; the choice still applies for this visit.
        }
    }

    /// <summary>The device switched between light and dark (only reported while <see cref="Mode"/> is System).</summary>
    public Task OnSystemDarkModeChangedAsync(bool isDark)
    {
        _systemDark = isDark;
        Apply();
        return Task.CompletedTask;
    }

    private void Apply()
    {
        IsDarkMode = Mode switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => _systemDark,
        };
        Changed?.Invoke();
    }

    private async Task<bool> TryReadSystemAsync()
    {
        try
        {
            return _readSystemDarkMode is not null && await _readSystemDarkMode();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// The saved choice. Before Phase 7a the app bar had a dark-mode toggle saved as a bool under
    /// <see cref="UserPreferences.DarkMode"/>: true → Dark, false → Light, never toggled → System.
    /// It's converted once and the old key removed.
    /// </summary>
    private async Task<ThemeMode> LoadModeAsync()
    {
        try
        {
            if (FromValue(await prefs.GetStringAsync(UserPreferences.Theme)) is { } saved)
            {
                return saved;
            }

            var legacy = await prefs.GetBoolAsync(UserPreferences.DarkMode);
            var mode = legacy switch
            {
                true => ThemeMode.Dark,
                false => ThemeMode.Light,
                null => ThemeMode.System,
            };

            if (legacy is not null)
            {
                await prefs.SetStringAsync(UserPreferences.Theme, ToValue(mode));
                await prefs.SetStringAsync(UserPreferences.DarkMode, null);
            }

            return mode;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Storage unavailable: follow the system.
            return ThemeMode.System;
        }
    }

    private static string ToValue(ThemeMode mode) => mode switch
    {
        ThemeMode.Light => LightValue,
        ThemeMode.Dark => DarkValue,
        _ => SystemValue,
    };

    private static ThemeMode? FromValue(string? value) => value switch
    {
        LightValue => ThemeMode.Light,
        DarkValue => ThemeMode.Dark,
        SystemValue => ThemeMode.System,
        _ => null,
    };
}
