namespace Bookmarks.UI.Services;

/// <summary>
/// Which browser and device the app is open in, for the install steps (Phase 8b). Chrome, Edge and Samsung Internet
/// can show their own install prompt (<see cref="IAppStatusService.CanPromptInstall"/>); Firefox and Safari can't, so
/// the app shows the steps instead.
/// </summary>
public enum InstallPlatform
{
    Unknown,
    AndroidChrome,
    AndroidSamsung,
    AndroidFirefox,
    AndroidOther,
    IosSafari,
    IosOther,
    DesktopChromium,
    DesktopFirefox,
    DesktopSafari,
}

public static class InstallPlatformExtensions
{
    /// <summary>A phone or tablet: where the install prompt is offered unasked.</summary>
    public static bool IsMobile(this InstallPlatform platform) => platform is InstallPlatform.AndroidChrome
        or InstallPlatform.AndroidSamsung or InstallPlatform.AndroidFirefox or InstallPlatform.AndroidOther
        or InstallPlatform.IosSafari or InstallPlatform.IosOther;
}
