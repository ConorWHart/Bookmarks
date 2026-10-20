using System.Globalization;
using System.Reflection;

namespace Bookmarks.UI.Services;

/// <summary>Formatting for the Settings page.</summary>
public static class SettingsText
{
    /// <summary>"1.0.0 (e68f10d)" from an informational version like "1.0.0+e68f10d2…" (the SDK appends the commit).</summary>
    public static string FormatVersion(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return "unknown";
        }

        var plus = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        if (plus < 0)
        {
            return informationalVersion;
        }

        var version = informationalVersion[..plus];
        var commit = informationalVersion[(plus + 1)..];
        return commit.Length == 0 ? version : $"{version} ({commit[..Math.Min(7, commit.Length)]})";
    }

    /// <summary>The app's informational version (the host assembly's; every project is built from the same commit).</summary>
    public static string? AppInformationalVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(SettingsText).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString();
    }

    /// <summary>"640 KB", "12.3 MB", "1.2 GB".</summary>
    public static string FormatBytes(long bytes)
    {
        string[] units = ["bytes", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var format = unit == 0 || value >= 100 ? "0" : "0.#";
        return value.ToString(format, CultureInfo.CurrentCulture) + " " + units[unit];
    }
}
