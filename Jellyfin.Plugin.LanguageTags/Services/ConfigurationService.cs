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
        => GetValidatedPrefix(Config.AudioLanguageTagPrefix, Config.SubtitleLanguageTagPrefix, "language_", "audio");

    /// <summary>
    /// Gets the validated subtitle language tag prefix.
    /// </summary>
    /// <returns>The validated subtitle language tag prefix.</returns>
    public string GetSubtitleLanguageTagPrefix()
        => GetValidatedPrefix(Config.SubtitleLanguageTagPrefix, Config.AudioLanguageTagPrefix, "subtitle_language_", "subtitle");

    /// <summary>
    /// Validates a prefix and ensures it's different from the other prefix.
    /// </summary>
    /// <param name="prefix">The prefix to validate.</param>
    /// <param name="otherPrefix">The other prefix to compare against.</param>
    /// <param name="defaultPrefix">The default prefix to use if validation fails.</param>
    /// <param name="prefixType">The type of prefix for logging.</param>
    /// <returns>The validated prefix.</returns>
    private string GetValidatedPrefix(string prefix, string otherPrefix, string defaultPrefix, string prefixType)
    {
        // Validate prefix: must be at least 3 characters
        if (string.IsNullOrWhiteSpace(prefix) || prefix.Length < 3)
        {
            return defaultPrefix;
        }

        // Ensure prefixes are different
        if (!string.IsNullOrWhiteSpace(otherPrefix) && prefix.Equals(otherPrefix, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Audio and subtitle prefixes cannot be identical. Using default {PrefixType} prefix '{DefaultPrefix}'",
                prefixType,
                defaultPrefix);
            return defaultPrefix;
        }

        return prefix;
    }
}
