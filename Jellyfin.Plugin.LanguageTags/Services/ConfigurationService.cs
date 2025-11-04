using System;
using Jellyfin.Plugin.LanguageTags.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LanguageTags.Services;

/// <summary>
/// Service for accessing plugin configuration with validation.
/// </summary>
public class ConfigurationService
{
    private readonly ILogger<ConfigurationService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationService"/> class.
    /// </summary>
    /// <param name="logger">Instance of the logger.</param>
    public ConfigurationService(ILogger<ConfigurationService> logger)
    {
        _logger = logger;
    }

    private PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

    /// <summary>
    /// Gets a value indicating whether full refresh should always be forced.
    /// </summary>
    public bool AlwaysForceFullRefresh => Config.AlwaysForceFullRefresh;

    /// <summary>
    /// Gets a value indicating whether synchronous refresh is enabled.
    /// </summary>
    public bool SynchronousRefresh => Config.SynchronousRefresh;

    /// <summary>
    /// Gets a value indicating whether subtitle tags should be added.
    /// </summary>
    public bool AddSubtitleTags => Config.AddSubtitleTags;

    /// <summary>
    /// Gets a value indicating whether undefined language tags should be disabled.
    /// </summary>
    public bool DisableUndefinedLanguageTags => Config.DisableUndefinedLanguageTags;

    /// <summary>
    /// Gets a value indicating whether non-media tagging is enabled.
    /// </summary>
    public bool EnableNonMediaTagging => Config.EnableNonMediaTagging;

    /// <summary>
    /// Gets the non-media tag name.
    /// </summary>
    public string NonMediaTag => Config.NonMediaTag ?? "item";

    /// <summary>
    /// Gets the non-media item types.
    /// </summary>
    public string NonMediaItemTypes => Config.NonMediaItemTypes ?? string.Empty;

    /// <summary>
    /// Gets the whitelist of language tags.
    /// </summary>
    public string WhitelistLanguageTags => Config.WhitelistLanguageTags ?? string.Empty;

    /// <summary>
    /// Gets the validated audio language tag prefix.
    /// </summary>
    /// <returns>The validated audio language tag prefix.</returns>
    public string GetAudioLanguageTagPrefix()
    {
        var prefix = Config.AudioLanguageTagPrefix;
        var subtitlePrefix = Config.SubtitleLanguageTagPrefix;

        // Validate prefix: must be at least 3 characters and different from subtitle prefix
        if (string.IsNullOrWhiteSpace(prefix) || prefix.Length < 3)
        {
            return "language_";
        }

        // Ensure audio and subtitle prefixes are different
        if (!string.IsNullOrWhiteSpace(subtitlePrefix) && prefix.Equals(subtitlePrefix, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Audio and subtitle prefixes cannot be identical. Using default audio prefix 'language_'");
            return "language_";
        }

        return prefix;
    }

    /// <summary>
    /// Gets the validated subtitle language tag prefix.
    /// </summary>
    /// <returns>The validated subtitle language tag prefix.</returns>
    public string GetSubtitleLanguageTagPrefix()
    {
        var prefix = Config.SubtitleLanguageTagPrefix;
        var audioPrefix = Config.AudioLanguageTagPrefix;

        // Validate prefix: must be at least 3 characters and different from audio prefix
        if (string.IsNullOrWhiteSpace(prefix) || prefix.Length < 3)
        {
            return "subtitle_language_";
        }

        // Ensure audio and subtitle prefixes are different
        if (!string.IsNullOrWhiteSpace(audioPrefix) && prefix.Equals(audioPrefix, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Audio and subtitle prefixes cannot be identical. Using default subtitle prefix 'subtitle_language_'");
            return "subtitle_language_";
        }

        return prefix;
    }
}
