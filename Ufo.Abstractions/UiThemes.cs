using System.Diagnostics.CodeAnalysis;

namespace Ufo.Abstractions;

/// <summary>
/// The themes the single-page application knows how to render. Kept as strings
/// rather than an enum so the value travels unchanged through SQLite (TEXT),
/// JSON and the Angular client, which uses them verbatim as CSS class names.
/// </summary>
public static class UiThemes
{
    public const string Light = "light";
    public const string Dark = "dark";

    /// <summary>
    /// What a user sees before they ever visit the Settings page. Dark, because
    /// that is what every component's stylesheet was written against.
    /// </summary>
    public const string Default = Dark;

    public static readonly IReadOnlyCollection<string> All = [Light, Dark];

    public static bool IsSupported([NotNullWhen(true)] string? theme) =>
        theme is not null && All.Contains(theme, StringComparer.Ordinal);
}
