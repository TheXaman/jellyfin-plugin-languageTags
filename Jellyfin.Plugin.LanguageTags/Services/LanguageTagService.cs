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
    /// Gets the language tag prefix for the specified tag type.
    /// </summary>
    /// <param name="type">The tag type.</param>
    /// <returns>The language tag prefix.</returns>
    private string GetLanguageTagPrefix(TagType type)
        => type == TagType.Audio ? _configService.GetAudioLanguageTagPrefix() : _configService.GetSubtitleLanguageTagPrefix();

    /// <summary>
    /// Checks if an item has language tags of the specified type.
    /// </summary>
    /// <param name="item">The item to check.</param>
    /// <param name="type">The tag type.</param>
    /// <returns>True if the item has language tags of the specified type.</returns>
    private bool HasLanguageTags(BaseItem item, TagType type)
    {
        var prefix = GetLanguageTagPrefix(type);
        return item.Tags.Any(tag => tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets language tags from an item for the specified type.
    /// </summary>
    /// <param name="item">The item to get tags from.</param>
    /// <param name="type">The tag type.</param>
    /// <returns>List of language tags.</returns>
    private List<string> GetLanguageTags(BaseItem item, TagType type)
    {
        var prefix = GetLanguageTagPrefix(type);
        return item.Tags.Where(tag => tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// Removes language tags from an item for the specified type.
    /// </summary>
    /// <param name="item">The item to remove tags from.</param>
    /// <param name="type">The tag type.</param>
    private void RemoveLanguageTags(BaseItem item, TagType type)
    {
        var prefix = GetLanguageTagPrefix(type);
        var tagsToRemove = item.Tags.Where(tag => tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();

        if (tagsToRemove.Count > 0)
        {
            item.Tags = item.Tags.Except(tagsToRemove, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    /// <summary>
    /// Internal method to add language tags to an item.
    /// </summary>
    /// <param name="item">The item to add tags to.</param>
    /// <param name="languages">List of languages.</param>
    /// <param name="isAudio">True if audio tags, false if subtitle tags.</param>
    /// <param name="convertFromIso">True to convert ISO codes to language names, false if already language names.</param>
    /// <returns>List of added languages.</returns>
    private List<string> AddLanguageTagsInternal(BaseItem item, List<string> languages, bool isAudio, bool convertFromIso)
    {
        if (convertFromIso)
        {
            languages = FilterOutLanguages(item, languages);
            languages = _conversionService.ConvertIsoToLanguageNames(languages);
        }

        languages = languages.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var prefix = isAudio ? _configService.GetAudioLanguageTagPrefix() : _configService.GetSubtitleLanguageTagPrefix();

        foreach (var languageName in languages)
        {
            string tag = $"{prefix}{languageName}";
            if (!item.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            {
                item.AddTag(tag);
            }
        }

        var parent = item.GetParent();
        _libraryManager.UpdateItemAsync(
            item: item,
            parent: parent,
            updateReason: ItemUpdateType.MetadataEdit,
            cancellationToken: CancellationToken.None).GetAwaiter().GetResult();

        return languages;
    }

    /// <summary>
    /// Checks if an item has audio language tags.
    /// </summary>
    /// <param name="item">The item to check.</param>
    /// <returns>True if the item has audio language tags.</returns>
    public bool HasAudioLanguageTags(BaseItem item)
        => HasLanguageTags(item, TagType.Audio);

    /// <summary>
    /// Checks if an item has subtitle language tags.
    /// </summary>
    /// <param name="item">The item to check.</param>
    /// <returns>True if the item has subtitle language tags.</returns>
    public bool HasSubtitleLanguageTags(BaseItem item)
        => HasLanguageTags(item, TagType.Subtitle);

    /// <summary>
    /// Gets audio language tags from an item.
    /// </summary>
    /// <param name="item">The item to get tags from.</param>
    /// <returns>List of audio language tags.</returns>
    public List<string> GetAudioLanguageTags(BaseItem item)
        => GetLanguageTags(item, TagType.Audio);

    /// <summary>
    /// Gets subtitle language tags from an item.
    /// </summary>
    /// <param name="item">The item to get tags from.</param>
    /// <returns>List of subtitle language tags.</returns>
    public List<string> GetSubtitleLanguageTags(BaseItem item)
        => GetLanguageTags(item, TagType.Subtitle);

    /// <summary>
    /// Removes audio language tags from an item.
    /// </summary>
    /// <param name="item">The item to remove tags from.</param>
    public void RemoveAudioLanguageTags(BaseItem item)
        => RemoveLanguageTags(item, TagType.Audio);

    /// <summary>
    /// Removes subtitle language tags from an item.
    /// </summary>
    /// <param name="item">The item to remove tags from.</param>
    public void RemoveSubtitleLanguageTags(BaseItem item)
        => RemoveLanguageTags(item, TagType.Subtitle);

    /// <summary>
    /// Strips the audio tag prefix from a list of tags.
    /// </summary>
    /// <param name="tags">The tags to strip the prefix from.</param>
    /// <returns>List of tags without the prefix.</returns>
    public List<string> StripAudioTagPrefix(IEnumerable<string> tags)
    {
        var prefix = _configService.GetAudioLanguageTagPrefix();
        return tags
            .Where(tag => tag.Length > prefix.Length)
            .Select(tag => tag.Substring(prefix.Length))
            .ToList();
    }

    /// <summary>
    /// Strips the subtitle tag prefix from a list of tags.
    /// </summary>
    /// <param name="tags">The tags to strip the prefix from.</param>
    /// <returns>List of tags without the prefix.</returns>
    public List<string> StripSubtitleTagPrefix(IEnumerable<string> tags)
    {
        var prefix = _configService.GetSubtitleLanguageTagPrefix();
        return tags
            .Where(tag => tag.Length > prefix.Length)
            .Select(tag => tag.Substring(prefix.Length))
            .ToList();
    }

    /// <summary>
    /// Filters out languages based on the whitelist configuration.
    /// </summary>
    /// <param name="item">The item being processed (for logging).</param>
    /// <param name="languages">List of language ISO codes to filter.</param>
    /// <returns>Filtered list of language ISO codes.</returns>
    public List<string> FilterOutLanguages(BaseItem item, List<string> languages)
    {
        // Get the whitelist of language tags
        var whitelist = _configService.WhitelistLanguageTags;
        var whitelistArray = whitelist.Split(Separator, StringSplitOptions.RemoveEmptyEntries).Select(lang => lang.Trim()).ToList();
        var filteredOutLanguages = new List<string>();

        // Check if whitelistArray has entries
        if (whitelistArray.Count > 0)
        {
            // Add und(efined) to the whitelist
            whitelistArray.Add("und");
            // Remove duplicates
            whitelistArray = whitelistArray.Distinct().ToList();
            // Remove invalid tags (not ISO 639-2/B language codes)
            whitelistArray = whitelistArray.Where(lang => lang.Length == 3).ToList();

            // Capture the filtered out languages
            filteredOutLanguages = languages.Where(lang => !whitelistArray.Contains(lang)).ToList();

            // Filter out tags that are not in the whitelist
            languages = languages.Where(lang => whitelistArray.Contains(lang)).ToList();
        }

        // Log filtered out languages
        if (filteredOutLanguages.Count > 0)
        {
            _logger.LogInformation("Filtered out languages for {ItemName}: {Languages}", item.Name, string.Join(", ", filteredOutLanguages));
        }

        return languages;
    }

    /// <summary>
    /// Adds audio language tags to an item, or undefined tag if no languages provided.
    /// </summary>
    /// <param name="item">The item to add tags to.</param>
    /// <param name="audioLanguages">List of audio language ISO codes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of added language ISO codes.</returns>
    public async Task<List<string>> AddAudioLanguageTagsOrUndefined(BaseItem item, List<string> audioLanguages, CancellationToken cancellationToken)
    {
        if (audioLanguages.Count > 0)
        {
            return await Task.Run(() => AddAudioLanguageTags(item, audioLanguages), cancellationToken).ConfigureAwait(false);
        }

        var disableUndTags = _configService.DisableUndefinedLanguageTags;
        if (!disableUndTags)
        {
            await Task.Run(() => AddAudioLanguageTags(item, new List<string> { "und" }), cancellationToken).ConfigureAwait(false);
            var prefix = _configService.GetAudioLanguageTagPrefix();
            _logger.LogWarning("No audio language information found for {ItemName}, added {Prefix}Undetermined", item.Name, prefix);
        }
        else
        {
            _logger.LogWarning("No audio language information found for {ItemName}, skipped adding undefined tags", item.Name);
        }

        return audioLanguages;
    }

    /// <summary>
    /// Adds audio language tags to an item (converts ISO codes to names).
    /// </summary>
    /// <param name="item">The item to add tags to.</param>
    /// <param name="languages">List of language ISO codes.</param>
    /// <returns>List of added language ISO codes.</returns>
    public List<string> AddAudioLanguageTags(BaseItem item, List<string> languages)
        => AddLanguageTagsInternal(item, languages, isAudio: true, convertFromIso: true);

    /// <summary>
    /// Adds audio language tags to an item (uses language names directly).
    /// </summary>
    /// <param name="item">The item to add tags to.</param>
    /// <param name="languages">List of language names.</param>
    /// <returns>List of added language names.</returns>
    public List<string> AddAudioLanguageTagsByName(BaseItem item, List<string> languages)
        => AddLanguageTagsInternal(item, languages, isAudio: true, convertFromIso: false);

    /// <summary>
    /// Adds subtitle language tags to an item (converts ISO codes to names).
    /// </summary>
    /// <param name="item">The item to add tags to.</param>
    /// <param name="languages">List of language ISO codes.</param>
    /// <returns>List of added language ISO codes.</returns>
    public List<string> AddSubtitleLanguageTags(BaseItem item, List<string> languages)
        => AddLanguageTagsInternal(item, languages, isAudio: false, convertFromIso: true);

    /// <summary>
    /// Adds subtitle language tags to an item (uses language names directly).
    /// </summary>
    /// <param name="item">The item to add tags to.</param>
    /// <param name="languages">List of language names.</param>
    /// <returns>List of added language names.</returns>
    public List<string> AddSubtitleLanguageTagsByName(BaseItem item, List<string> languages)
        => AddLanguageTagsInternal(item, languages, isAudio: false, convertFromIso: false);
}
