namespace Jellyfin.Plugin.LanguageTags;

/// <summary>
/// Represents information about a language with its various ISO codes.
/// </summary>
public class LanguageInfo
{
    /// <summary>
    /// Gets or sets the ISO 639-2 language code (3 letters).
    /// </summary>
    public string? Iso6392 { get; set; }

    /// <summary>
    /// Gets or sets the ISO 639-2/B bibliographic language code (3 letters).
    /// </summary>
    public string? Iso6392B { get; set; }

    /// <summary>
    /// Gets or sets the ISO 639-1 language code (2 letters).
    /// </summary>
    public string? Iso6391 { get; set; }

    /// <summary>
    /// Gets or sets the English name of the language.
    /// </summary>
    public string Name { get; set; } = string.Empty;
}
