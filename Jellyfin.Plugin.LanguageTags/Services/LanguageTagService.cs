using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LanguageTags.Services;

/// <summary>
/// Type of language tag (audio or subtitle).
/// </summary>
public enum TagType
{
    /// <summary>
    /// Audio language tag.
    /// </summary>
    Audio,

    /// <summary>
    /// Subtitle language tag.
    /// </summary>
    Subtitle
}

/// <summary>
/// Service for managing language tags on library items.
/// </summary>
public class LanguageTagService
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<LanguageTagService> _logger;
    private readonly ConfigurationService _configService;
    private readonly LanguageConversionService _conversionService;
    private static readonly char[] Separator = new[] { ',' };

    /// <summary>
    /// Initializes a new instance of the <see cref="LanguageTagService"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the library manager.</param>
    /// <param name="logger">Instance of the logger.</param>
    /// <param name="configService">Instance of the configuration service.</param>
    /// <param name="conversionService">Instance of the language conversion service.</param>
    public LanguageTagService(
        ILibraryManager libraryManager,
        ILogger<LanguageTagService> logger,
        ConfigurationService configService,
        LanguageConversionService conversionService)
    {
        _libraryManager = libraryManager;
        _logger = logger;
        _configService = configService;
        _conversionService = conversionService;
    }

    /// <summary>
    /// Checks if an item has language tags of the specified type.
    /// </summary>
    /// <param name="item">The item to check.</param>
    /// <param name="type">The tag type (Audio or Subtitle).</param>
    /// <param name="audioPrefix">The audio prefix to use.</param>
    /// <param name="subtitlePrefix">The subtitle prefix to use.</param>
    /// <returns>True if the item has language tags of the specified type.</returns>
    public bool HasLanguageTags(BaseItem item, TagType type, string audioPrefix, string subtitlePrefix)
    {
        var prefix = type == TagType.Audio ? audioPrefix : subtitlePrefix;
        return item.Tags.Any(tag => tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets language tags from an item for the specified type.
    /// </summary>
    /// <param name="item">The item to get tags from.</param>
    /// <param name="type">The tag type (Audio or Subtitle).</param>
    /// <param name="audioPrefix">The audio prefix to use.</param>
    /// <param name="subtitlePrefix">The subtitle prefix to use.</param>
    /// <returns>List of language tags.</returns>
    public List<string> GetLanguageTags(BaseItem item, TagType type, string audioPrefix, string subtitlePrefix)
    {
        var prefix = type == TagType.Audio ? audioPrefix : subtitlePrefix;
        return item.Tags.Where(tag => tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// Removes language tags from an item for the specified type.
    /// </summary>
    /// <param name="item">The item to remove tags from.</param>
    /// <param name="type">The tag type (Audio or Subtitle).</param>
    /// <param name="audioPrefix">The audio prefix to use.</param>
    /// <param name="subtitlePrefix">The subtitle prefix to use.</param>
    public void RemoveLanguageTags(BaseItem item, TagType type, string audioPrefix, string subtitlePrefix)
    {
        var prefix = type == TagType.Audio ? audioPrefix : subtitlePrefix;
        var tagsToRemove = item.Tags.Where(tag => tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();

        if (tagsToRemove.Count > 0)
        {
            item.Tags = item.Tags.Except(tagsToRemove, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    /// <summary>
    /// Adds language tags to an item with provided prefixes and whitelist.
    /// </summary>
    /// <param name="item">The item to add tags to.</param>
    /// <param name="languages">List of languages.</param>
    /// <param name="type">The tag type (Audio or Subtitle).</param>
    /// <param name="convertFromIso">True to convert ISO codes to language names, false if already language names.</param>
    /// <param name="audioPrefix">The audio prefix to use.</param>
    /// <param name="subtitlePrefix">The subtitle prefix to use.</param>
    /// <param name="whitelist">The whitelist to use for filtering.</param>
    /// <returns>List of added languages.</returns>
    public List<string> AddLanguageTags(BaseItem item, List<string> languages, TagType type, bool convertFromIso, string audioPrefix, string subtitlePrefix, List<string> whitelist)
    {
        // Make sure languages are unique
        languages = languages.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (convertFromIso)
        {
            languages = FilterOutLanguages(item, languages, whitelist);
            languages = _conversionService.ConvertIsoToLanguageNames(languages);
        }

        languages = languages.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var prefix = type == TagType.Audio ? audioPrefix : subtitlePrefix;

        var newAddedLanguages = new List<string>();
        foreach (var languageName in languages)
        {
            string tag = $"{prefix}{languageName}";
            if (!item.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            {
                item.AddTag(tag);
                newAddedLanguages.Add(languageName);
            }
        }

        return newAddedLanguages;
    }

    /// <summary>
    /// Strips the tag prefix from a list of tags for the specified type.
    /// </summary>
    /// <param name="tags">The tags to strip the prefix from.</param>
    /// <param name="type">The tag type to get the prefix for.</param>
    /// <param name="audioPrefix">The audio prefix to use.</param>
    /// <param name="subtitlePrefix">The subtitle prefix to use.</param>
    /// <returns>List of tags without the prefix.</returns>
    public List<string> StripTagPrefix(IEnumerable<string> tags, TagType type, string audioPrefix, string subtitlePrefix)
    {
        var prefix = type == TagType.Audio ? audioPrefix : subtitlePrefix;
        return tags
            .Where(tag => tag.Length > prefix.Length)
            .Select(tag => tag.Substring(prefix.Length))
            .ToList();
    }

    /// <summary>
    /// Filters out languages based on provided whitelist.
    /// </summary>
    /// <param name="item">The item being processed (for logging).</param>
    /// <param name="languages">List of language ISO codes to filter.</param>
    /// <param name="whitelist">The whitelist to use.</param>
    /// <returns>Filtered list of language ISO codes.</returns>
    public List<string> FilterOutLanguages(BaseItem item, List<string> languages, List<string> whitelist)
    {
        if (whitelist.Count == 0)
        {
            return languages;
        }

        var filteredOutLanguages = languages.Except(whitelist).ToList();
        var filteredLanguages = languages.Intersect(whitelist).ToList();

        if (filteredOutLanguages.Count > 0)
        {
            _logger.LogInformation(
                "Filtered out languages for {ItemName}: {Languages}",
                item.Name,
                string.Join(", ", filteredOutLanguages));
        }

        return filteredLanguages;
    }

    /// <summary>
    /// Parses and validates a whitelist string.
    /// </summary>
    /// <param name="whitelistString">The whitelist string to parse.</param>
    /// <returns>List of valid language codes.</returns>
    public static List<string> ParseWhitelist(string whitelistString)
    {
        if (string.IsNullOrWhiteSpace(whitelistString))
        {
            return new List<string>();
        }

        var undArray = new[] { "und" };
        return whitelistString.Split(Separator, StringSplitOptions.RemoveEmptyEntries)
            .Select(lang => lang.Trim())
            .Where(lang => lang.Length == 3) // Valid ISO 639-2/B codes
            .Distinct()
            .Concat(undArray) // Always include "undefined"
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Adds audio language tags to an item, or undefined tag if no languages provided, using provided prefixes and whitelist.
    /// </summary>
    /// <param name="item">The item to add tags to.</param>
    /// <param name="audioLanguages">List of audio language ISO codes.</param>
    /// <param name="audioPrefix">The audio prefix to use.</param>
    /// <param name="subtitlePrefix">The subtitle prefix to use.</param>
    /// <param name="whitelist">The whitelist to use for filtering.</param>
    /// <param name="disableUndefinedTags">Whether undefined tags are disabled.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of added language ISO codes.</returns>
    public async Task<List<string>> AddAudioLanguageTagsOrUndefined(BaseItem item, List<string> audioLanguages, string audioPrefix, string subtitlePrefix, List<string> whitelist, bool disableUndefinedTags, CancellationToken cancellationToken)
    {
        if (audioLanguages.Count > 0)
        {
            return await Task.Run(() => AddLanguageTags(item, audioLanguages, TagType.Audio, convertFromIso: true, audioPrefix, subtitlePrefix, whitelist), cancellationToken).ConfigureAwait(false);
        }

        if (!disableUndefinedTags)
        {
            await Task.Run(() => AddLanguageTags(item, new List<string> { "und" }, TagType.Audio, convertFromIso: true, audioPrefix, subtitlePrefix, whitelist), cancellationToken).ConfigureAwait(false);
            _logger.LogWarning("No audio language information found for {ItemName}, added {Prefix}Undetermined", item.Name, audioPrefix);
        }
        else
        {
            _logger.LogWarning("No audio language information found for {ItemName}, skipped adding undefined tags", item.Name);
        }

        return audioLanguages;
    }
}
