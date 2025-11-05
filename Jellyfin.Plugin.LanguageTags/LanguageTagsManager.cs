using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Extensions;
using Jellyfin.Plugin.LanguageTags.Services;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LanguageTags;

/// <summary>
/// Class LanguageTagsManager.
/// </summary>
public class LanguageTagsManager : IHostedService, IDisposable
{
    private readonly ILibraryManager _libraryManager;
    private readonly ICollectionManager _collectionManager;
    private readonly ILogger<LanguageTagsManager> _logger;
    private readonly ConfigurationService _configService;
    private readonly LanguageConversionService _conversionService;
    private readonly LanguageTagService _tagService;
    private readonly LibraryQueryService _queryService;
    private readonly SubtitleExtractionService _subtitleService;

    /// <summary>
    /// Initializes a new instance of the <see cref="LanguageTagsManager"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="collectionManager">Instance of the <see cref="ICollectionManager"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{LanguageTagsManager}"/> interface.</param>
    /// <param name="configService">Instance of the configuration service.</param>
    /// <param name="conversionService">Instance of the language conversion service.</param>
    /// <param name="tagService">Instance of the language tag service.</param>
    /// <param name="queryService">Instance of the library query service.</param>
    /// <param name="subtitleService">Instance of the subtitle extraction service.</param>
    public LanguageTagsManager(
        ILibraryManager libraryManager,
        ICollectionManager collectionManager,
        ILogger<LanguageTagsManager> logger,
        ConfigurationService configService,
        LanguageConversionService conversionService,
        LanguageTagService tagService,
        LibraryQueryService queryService,
        SubtitleExtractionService subtitleService)
    {
        _libraryManager = libraryManager;
        _collectionManager = collectionManager;
        _logger = logger;
        _configService = configService;
        _conversionService = conversionService;
        _tagService = tagService;
        _queryService = queryService;
        _subtitleService = subtitleService;
    }

    /// <summary>
    /// Generic helper method to process items either asynchronously (parallel) or synchronously.
    /// </summary>
    /// <typeparam name="T">The type of items to process.</typeparam>
    /// <param name="items">List of items to process.</param>
    /// <param name="processor">Function to process each item, returns true if processed, false if skipped.</param>
    /// <param name="synchronously">If true, process items synchronously; if false, process in parallel.</param>
    /// <returns>Tuple of (processed count, skipped count).</returns>
    private async Task<(int Processed, int Skipped)> ProcessItemsAsync<T>(
        List<T> items,
        Func<T, CancellationToken, Task<bool>> processor,
        bool synchronously)
    {
        int processed = 0;
        int skipped = 0;

        if (!synchronously)
        {
            await Parallel.ForEachAsync(items, async (item, ct) =>
            {
                var wasProcessed = await processor(item, ct).ConfigureAwait(false);
                if (wasProcessed)
                {
                    Interlocked.Increment(ref processed);
                }
                else
                {
                    Interlocked.Increment(ref skipped);
                }
            }).ConfigureAwait(false);
        }
        else
        {
            foreach (var item in items)
            {
                var wasProcessed = await processor(item, CancellationToken.None).ConfigureAwait(false);
                if (wasProcessed)
                {
                    processed++;
                }
                else
                {
                    skipped++;
                }
            }
        }

        return (processed, skipped);
    }

    /// <summary>
    /// Common method to handle tag checking, removal and processing decision for video items.
    /// </summary>
    /// <param name="video">The video item to check.</param>
    /// <param name="fullScan">Whether this is a full scan.</param>
    /// <param name="subtitleTags">Whether subtitle processing is enabled.</param>
    /// <returns>Tuple indicating if video should be processed and any existing languages found.</returns>
    private (bool ShouldProcess, List<string> ExistingAudio, List<string> ExistingSubtitle) CheckAndPrepareVideoForProcessing(
        Video video, bool fullScan, bool subtitleTags)
    {
        bool shouldProcess = fullScan;
        var existingAudio = new List<string>();
        var existingSubtitle = new List<string>();

        // Check audio tags
        var hasAudioTags = _tagService.HasLanguageTags(video, TagType.Audio);
        if (hasAudioTags)
        {
            if (fullScan)
            {
                _tagService.RemoveLanguageTags(video, TagType.Audio);
            }
            else
            {
                var audioNames = _tagService.StripTagPrefix(_tagService.GetLanguageTags(video, TagType.Audio), TagType.Audio);
                existingAudio = _conversionService.ConvertLanguageNamesToIso(audioNames);
            }
        }
        else
        {
            shouldProcess = true;
        }

        // Check subtitle tags
        if (subtitleTags)
        {
            var hasSubtitleTags = _tagService.HasLanguageTags(video, TagType.Subtitle);
            if (hasSubtitleTags)
            {
                if (fullScan)
                {
                    _tagService.RemoveLanguageTags(video, TagType.Subtitle);
                }
                else
                {
                    var subtitleNames = _tagService.StripTagPrefix(_tagService.GetLanguageTags(video, TagType.Subtitle), TagType.Subtitle);
                    existingSubtitle = _conversionService.ConvertLanguageNamesToIso(subtitleNames);
                }
            }
            else
            {
                shouldProcess = true;
            }
        }

        return (shouldProcess, existingAudio, existingSubtitle);
    }

    /// <summary>
    /// Common method to apply language tags to an item and save to repository.
    /// </summary>
    /// <param name="item">The item to apply tags to.</param>
    /// <param name="audioLanguages">List of audio language codes.</param>
    /// <param name="subtitleLanguages">List of subtitle language codes.</param>
    /// <param name="subtitleTags">Whether subtitle processing is enabled.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task representing the operation.</returns>
    private async Task ApplyLanguageTagsAndSave(
        BaseItem item,
        List<string> audioLanguages,
        List<string> subtitleLanguages,
        bool subtitleTags,
        CancellationToken cancellationToken)
    {
        // Remove existing tags
        _tagService.RemoveLanguageTags(item, TagType.Audio);
        _tagService.RemoveLanguageTags(item, TagType.Subtitle);

        // Make languages unique
        audioLanguages = audioLanguages.Distinct().ToList();
        subtitleLanguages = subtitleLanguages.Distinct().ToList();

        // Add audio tags
        audioLanguages = await _tagService.AddAudioLanguageTagsOrUndefined(item, audioLanguages, cancellationToken).ConfigureAwait(false);
        if (audioLanguages.Count > 0)
        {
            _logger.LogInformation("Added audio tags for {ItemName}: {Languages}", item.Name, string.Join(", ", audioLanguages));
        }

        // Add subtitle tags
        if (subtitleTags && subtitleLanguages.Count > 0)
        {
            subtitleLanguages = await Task.Run(() => _tagService.AddLanguageTags(item, subtitleLanguages, TagType.Subtitle, convertFromIso: true), cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Added subtitle tags for {ItemName}: {Languages}", item.Name, string.Join(", ", subtitleLanguages));
        }
        else if (subtitleTags)
        {
            _logger.LogWarning("No subtitle information found for {ItemName}", item.Name);
        }

        // Save to repository
        await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Scans the library.
    /// </summary>
    /// <param name="fullScan">if set to <c>true</c> [full scan].</param>
    /// <param name="type">The type of refresh to perform. Default is "everything".</param>
    /// <returns>A <see cref="Task"/> representing the library scan progress.</returns>
    public async Task ScanLibrary(bool fullScan = false, string type = "everything")
    {
        // Get configuration values
        fullScan = fullScan || _configService.AlwaysForceFullRefresh;
        var synchronously = _configService.SynchronousRefresh;
        var subtitleTags = _configService.AddSubtitleTags;

        LogScanConfiguration(fullScan, synchronously, subtitleTags);

        // Process the libraries
        switch (type.ToLowerInvariant())
        {
            case "movies":
                await ProcessLibraryMovies(fullScan, synchronously, subtitleTags).ConfigureAwait(false);
                break;
            case "series":
            case "tvshows":
                await ProcessLibrarySeries(fullScan, synchronously, subtitleTags).ConfigureAwait(false);
                break;
            case "collections":
                await ProcessLibraryCollections(fullScan, synchronously, subtitleTags).ConfigureAwait(false);
                break;
            case "externalsubtitles":
                await ProcessLibraryExternalSubtitles(synchronously, subtitleTags).ConfigureAwait(false);
                break;
            default:
                await ProcessAllLibraryTypes(fullScan, synchronously, subtitleTags).ConfigureAwait(false);
                break;
        }
    }

    /// <summary>
    /// Logs the current scan configuration.
    /// </summary>
    private void LogScanConfiguration(bool fullScan, bool synchronously, bool subtitleTags)
    {
        if (fullScan)
        {
            _logger.LogInformation("Full scan enabled");
        }

        if (synchronously)
        {
            _logger.LogInformation("Synchronous refresh enabled");
        }

        if (subtitleTags)
        {
            _logger.LogInformation("Extract subtitle languages enabled");
        }
    }

    /// <summary>
    /// Processes all library types in sequence.
    /// </summary>
    private async Task ProcessAllLibraryTypes(bool fullScan, bool synchronously, bool subtitleTags)
    {
        await ProcessLibraryMovies(fullScan, synchronously, subtitleTags).ConfigureAwait(false);
        await ProcessLibrarySeries(fullScan, synchronously, subtitleTags).ConfigureAwait(false);
        await ProcessLibraryCollections(fullScan, synchronously, subtitleTags).ConfigureAwait(false);
        await ProcessLibraryExternalSubtitles(synchronously, subtitleTags).ConfigureAwait(false);
        await ProcessNonMediaItems().ConfigureAwait(false);
    }

    /// <summary>
    /// Removes all language tags from all content in the library.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the removal process.</returns>
    public async Task RemoveAllLanguageTags()
    {
        _logger.LogInformation("Starting removal of all language tags from library");

        try
        {
            var itemTypesToRemove = new[]
            {
                (BaseItemKind.Movie, "movies"),
                (BaseItemKind.Episode, "episodes"),
                (BaseItemKind.Season, "seasons"),
                (BaseItemKind.Series, "series"),
                (BaseItemKind.BoxSet, "collections")
            };

            foreach (var (itemKind, itemTypeName) in itemTypesToRemove)
            {
                await RemoveLanguageTagsFromItemType(itemKind, itemTypeName).ConfigureAwait(false);
            }

            _logger.LogInformation("Completed removal of all language tags from library");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing all language tags from library");
            throw;
        }
    }

    /// <summary>
    /// Removes language tags from items of a specific type.
    /// </summary>
    /// <param name="itemKind">The kind of item to remove tags from.</param>
    /// <param name="itemTypeName">The name of the item type for logging.</param>
    /// <returns>A <see cref="Task"/> representing the removal process.</returns>
    private async Task RemoveLanguageTagsFromItemType(BaseItemKind itemKind, string itemTypeName)
    {
        var items = _libraryManager.QueryItems(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { itemKind },
            Recursive = true
        }).Items;

        _logger.LogInformation("Removing language tags from {Count} {Type}", items.Count, itemTypeName);

        foreach (var item in items)
        {
            _tagService.RemoveLanguageTags(item, TagType.Audio);
            _tagService.RemoveLanguageTags(item, TagType.Subtitle);
            await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Processes non-media items and applies tags.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the processing.</returns>
    public async Task ProcessNonMediaItems()
    {
        if (!_configService.EnableNonMediaTagging)
        {
            _logger.LogInformation("Non-media tagging is disabled");
            return;
        }

        var tagName = _configService.NonMediaTag;
        var itemTypes = GetConfiguredItemTypes(_configService.NonMediaItemTypes);

        if (itemTypes.Count == 0)
        {
            _logger.LogInformation("No non-media item types selected for tagging");
            return;
        }

        _logger.LogInformation("Applying tag '{TagName}' to {Count} item types", tagName, itemTypes.Count);
        LogProcessingHeader("Processing non-media items...");

        foreach (var itemType in itemTypes)
        {
            await ProcessNonMediaItemType(itemType, tagName).ConfigureAwait(false);
        }

        _logger.LogInformation("Completed non-media item tagging");
    }

    /// <summary>
    /// Removes non-media tags from all items.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the removal process.</returns>
    public async Task RemoveNonMediaTags()
    {
        var tagName = _configService.NonMediaTag;
        var itemTypes = GetConfiguredItemTypes(_configService.NonMediaItemTypes);

        _logger.LogInformation("Starting removal of non-media tag '{TagName}' from library", tagName);

        if (itemTypes.Count == 0)
        {
            _logger.LogWarning("No non-media item types configured for tag removal");
            return;
        }

        try
        {
            foreach (var itemType in itemTypes)
            {
                await RemoveNonMediaTagFromItemType(itemType, tagName).ConfigureAwait(false);
            }

            _logger.LogInformation("Completed removal of non-media tags");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing non-media tags from library");
            throw;
        }
    }

    /// <summary>
    /// Gets configured item types from a comma-separated string.
    /// </summary>
    private static List<string> GetConfiguredItemTypes(string itemTypesString)
    {
        return itemTypesString.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();
    }

    /// <summary>
    /// Logs a processing header with decorative borders.
    /// </summary>
    private void LogProcessingHeader(string message)
    {
        var border = new string('*', message.Length + 6);
        _logger.LogInformation("{Border}", border);
        _logger.LogInformation("*  {Message}   *", message);
        _logger.LogInformation("{Border}", border);
    }

    /// <summary>
    /// Processes a single non-media item type for tagging.
    /// </summary>
    private async Task ProcessNonMediaItemType(string itemType, string tagName)
    {
        try
        {
            if (!Enum.TryParse<BaseItemKind>(itemType, out var kind))
            {
                _logger.LogWarning("Unknown item type: {ItemType}", itemType);
                return;
            }

            var items = _libraryManager.QueryItems(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { kind },
                Recursive = true
            }).Items;

            _logger.LogInformation("Found {Count} {ItemType} items", items.Count, itemType);

            int taggedCount = 0;
            foreach (var item in items)
            {
                if (!item.Tags.Contains(tagName, StringComparer.OrdinalIgnoreCase))
                {
                    var tagsList = item.Tags.ToList();
                    tagsList.Add(tagName);
                    item.Tags = tagsList.ToArray();
                    await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None)
                        .ConfigureAwait(false);
                    taggedCount++;
                }
            }

            _logger.LogInformation("Tagged {TaggedCount} of {TotalCount} {ItemType} items", taggedCount, items.Count, itemType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing non-media items of type {ItemType}", itemType);
        }
    }

    /// <summary>
    /// Removes non-media tags from a specific item type.
    /// </summary>
    private async Task RemoveNonMediaTagFromItemType(string itemType, string tagName)
    {
        if (!Enum.TryParse<BaseItemKind>(itemType, out var kind))
        {
            return;
        }

        var items = _libraryManager.QueryItems(new InternalItemsQuery
        {
            IncludeItemTypes = new[] { kind },
            Recursive = true
        }).Items;

        _logger.LogInformation("Removing tag from {Count} {ItemType} items", items.Count, itemType);

        int removedCount = 0;
        foreach (var item in items)
        {
            var originalCount = item.Tags.Length;
            item.Tags = item.Tags.Where(t =>
                !t.Equals(tagName, StringComparison.OrdinalIgnoreCase)).ToArray();

            if (item.Tags.Length < originalCount)
            {
                await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None)
                    .ConfigureAwait(false);
                removedCount++;
            }
        }

        _logger.LogInformation("Removed tag from {RemovedCount} {ItemType} items", removedCount, itemType);
    }

    /// <summary>
    /// Processes the libraries movies.
    /// </summary>
    /// <param name="fullScan">if set to <c>true</c> [full scan].</param>
    /// <param name="synchronously">if set to <c>true</c> [synchronously].</param>
    /// <param name="subtitleTags">if set to <c>true</c> [extract subtitle languages].</param>
    /// <returns>A <see cref="Task"/> representing the library scan progress.</returns>
    private async Task ProcessLibraryMovies(bool fullScan, bool synchronously, bool subtitleTags)
    {
        LogProcessingHeader("Processing movies...");

        var movies = _queryService.GetMoviesFromLibrary();
        var (moviesProcessed, moviesSkipped) = await ProcessItemsAsync(
            movies,
            async (movie, ct) => await ProcessMovie(movie, fullScan, subtitleTags, ct).ConfigureAwait(false),
            synchronously).ConfigureAwait(false);

        _logger.LogInformation(
            "MOVIES - processed {Processed} of {Total} ({Skipped} skipped)",
            moviesProcessed,
            movies.Count,
            moviesSkipped);
    }

    private async Task<bool> ProcessMovie(Movie movie, bool fullScan, bool subtitleTags, CancellationToken cancellationToken)
    {
        if (movie is not Video video)
        {
            return false;
        }

        var (shouldProcess, existingAudio, existingSubtitle) = CheckAndPrepareVideoForProcessing(video, fullScan, subtitleTags);

        if (shouldProcess)
        {
            var (audioLanguages, subtitleLanguages) = await ProcessVideo(video, subtitleTags, cancellationToken).ConfigureAwait(false);

        if (audioLanguages.Count > 0 || subtitleLanguages.Count > 0)
            {
                _logger.LogInformation(
                    "MOVIE - {MovieName} - audio: {Audio} - subtitles: {Subtitles}",
                    movie.Name,
                    audioLanguages.Count > 0 ? string.Join(", ", audioLanguages) : "none",
                    subtitleLanguages.Count > 0 ? string.Join(", ", subtitleLanguages) : "none");
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Processes the libraries series.
    /// </summary>
    /// <param name="fullScan">if set to <c>true</c> [full scan].</param>
    /// <param name="synchronously">if set to <c>true</c> [synchronously].</param>
    /// <param name="subtitleTags">if set to <c>true</c> [extract subtitle languages].</param>
    /// <returns>A <see cref="Task"/> representing the library scan progress.</returns>
    private async Task ProcessLibrarySeries(bool fullScan, bool synchronously, bool subtitleTags)
    {
        LogProcessingHeader("Processing series...");

        var seriesList = _queryService.GetSeriesFromLibrary();
        var (processedSeries, skippedSeries) = await ProcessItemsAsync(
            seriesList,
            async (seriesBaseItem, ct) =>
            {
                if (seriesBaseItem is Series series)
                {
                    await ProcessSeries(series, fullScan, subtitleTags, ct).ConfigureAwait(false);
                    return true;
                }
                else
                {
                    _logger.LogWarning("Series is null!");
                    return false;
                }
            },
            synchronously).ConfigureAwait(false);

        _logger.LogInformation(
            "SERIES - processed {Processed} of {Total} ({Skipped} skipped)",
            processedSeries,
            seriesList.Count,
            skippedSeries);
    }

    private async Task ProcessSeries(Series series, bool fullScan, bool subtitleTags, CancellationToken cancellationToken)
    {
        // Get all seasons in the series
        var seasons = _queryService.GetSeasonsFromSeries(series);
        if (seasons == null || seasons.Count == 0)
        {
            _logger.LogWarning("No seasons found in SERIES {SeriesName}", series.Name);
            return;
        }

        // Get language tags from all seasons in the series
        _logger.LogInformation("Processing SERIES {SeriesName}", series.Name);
        var seriesAudioLanguages = new List<string>();
        var seriesSubtitleLanguages = new List<string>();
        foreach (var season in seasons)
        {
            var episodes = _queryService.GetEpisodesFromSeason(season);

            if (episodes == null || episodes.Count == 0)
            {
                _logger.LogWarning("No episodes found in SEASON {SeasonName} of {SeriesName}", season.Name, series.Name);
                continue;
            }

            // Get language tags from all episodes in the season
            _logger.LogDebug("Processing SEASON {SeasonName} of {SeriesName}", season.Name, series.Name);
            var seasonAudioLanguages = new List<string>();
            var seasonSubtitleLanguages = new List<string>();
            int episodesProcessed = 0;
            int episodesSkipped = 0;

            foreach (var episode in episodes)
            {
                if (episode is Video video)
                {
                    bool shouldProcessVideo = fullScan;
                    bool hasExistingAudioTags = _tagService.HasLanguageTags(video, TagType.Audio);
                    bool hasExistingSubtitleTags = _tagService.HasLanguageTags(video, TagType.Subtitle);

                    // Check if the video has subtitle language tags and subtitleTags is enabled
                    if (hasExistingSubtitleTags && subtitleTags)
                    {
                        if (!fullScan)
                        {
                            _logger.LogDebug("Subtitle tags exist for {VideoName}", video.Name);
                            var episodeLanguageNames = _tagService.StripTagPrefix(_tagService.GetLanguageTags(video, TagType.Subtitle), TagType.Subtitle);
                            // Convert language names back to ISO codes for consistent processing
                            var episodeLanguagesTmp = _conversionService.ConvertLanguageNamesToIso(episodeLanguageNames);
                            seasonSubtitleLanguages.AddRange(episodeLanguagesTmp);
                        }
                        else
                        {
                            _tagService.RemoveLanguageTags(episode, TagType.Subtitle);
                        }
                    }
                    else if (subtitleTags)
                    {
                        // No subtitle tags exist, need to process
                        shouldProcessVideo = true;
                    }

                    // Check if the video has audio language tags
                    if (hasExistingAudioTags)
                    {
                        if (!fullScan)
                        {
                            _logger.LogDebug("Audio tags exist for {VideoName}", video.Name);
                            var episodeLanguageNames = _tagService.StripTagPrefix(_tagService.GetLanguageTags(video, TagType.Audio), TagType.Audio);
                            // Convert language names back to ISO codes for consistent processing
                            var episodeLanguagesTmp = _conversionService.ConvertLanguageNamesToIso(episodeLanguageNames);
                            seasonAudioLanguages.AddRange(episodeLanguagesTmp);
                        }
                        else
                        {
                            _tagService.RemoveLanguageTags(episode, TagType.Audio);
                        }
                    }
                    else
                    {
                        // No audio tags exist, need to process
                        shouldProcessVideo = true;
                    }

                    // Process the video if needed
                    if (shouldProcessVideo)
                    {
                        var (audioLanguages, subtitleLanguages) = await ProcessVideo(video, subtitleTags, cancellationToken).ConfigureAwait(false);
                        seasonAudioLanguages.AddRange(audioLanguages);
                        seasonSubtitleLanguages.AddRange(subtitleLanguages);
                        episodesProcessed++;
                    }
                    else
                    {
                        episodesSkipped++;
                    }
                }
            }

            // Make sure we have unique language tags
            seasonAudioLanguages = seasonAudioLanguages.Distinct().ToList();
            seasonSubtitleLanguages = seasonSubtitleLanguages.Distinct().ToList();

            // Add the season languages to the series languages
            seriesAudioLanguages.AddRange(seasonAudioLanguages);
            seriesSubtitleLanguages.AddRange(seasonSubtitleLanguages);

            // Remove existing language tags
            _tagService.RemoveLanguageTags(season, TagType.Audio);
            _tagService.RemoveLanguageTags(season, TagType.Subtitle);

            // Add audio language tags to the season
            seasonAudioLanguages = await _tagService.AddAudioLanguageTagsOrUndefined(season, seasonAudioLanguages, cancellationToken).ConfigureAwait(false);

            // Add subtitle language tags to the season
            if (seasonSubtitleLanguages.Count > 0 && subtitleTags)
            {
                seasonSubtitleLanguages = await Task.Run(() => _tagService.AddLanguageTags(season, seasonSubtitleLanguages, TagType.Subtitle, convertFromIso: true), cancellationToken).ConfigureAwait(false);
            }

            // Log season-level summary, only if languages were found
            if (episodesProcessed > 0 && ( seasonAudioLanguages.Count > 0 || seasonSubtitleLanguages.Count > 0))
            {
                _logger.LogInformation(
                    "  SEASON - {SeriesName} - {SeasonName} - processed {Processed} episodes of {Total} ({Skipped} skipped) - audio: {Audio} - subtitles: {Subtitles}",
                    series.Name,
                    season.Name,
                    episodesProcessed,
                    episodes.Count,
                    episodesSkipped,
                    seasonAudioLanguages.Count > 0 ? string.Join(", ", seasonAudioLanguages) : "none",
                    seasonSubtitleLanguages.Count > 0 ? string.Join(", ", seasonSubtitleLanguages) : "none");
            }

            // Save season changes to repository
            await season.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
        }

        // Remove existing language tags
        _tagService.RemoveLanguageTags(series, TagType.Audio);
        _tagService.RemoveLanguageTags(series, TagType.Subtitle);

        // Make sure we have unique language tags
        seriesAudioLanguages = seriesAudioLanguages.Distinct().ToList();
        seriesSubtitleLanguages = seriesSubtitleLanguages.Distinct().ToList();

        // Add audio language tags to the series
        seriesAudioLanguages = await _tagService.AddAudioLanguageTagsOrUndefined(series, seriesAudioLanguages, cancellationToken).ConfigureAwait(false);

        // Add subtitle language tags to the series
        if (seriesSubtitleLanguages.Count > 0 && subtitleTags)
        {
            seriesSubtitleLanguages = await Task.Run(() => _tagService.AddLanguageTags(series, seriesSubtitleLanguages, TagType.Subtitle, convertFromIso: true), cancellationToken).ConfigureAwait(false);
        }

        // Log series-level summary, only if languages were found
        if (seriesAudioLanguages.Count > 0 || seriesSubtitleLanguages.Count > 0)
        {
            _logger.LogInformation(
                "SERIES - {SeriesName} - audio: {Audio} - subtitles: {Subtitles}",
                series.Name,
                seriesAudioLanguages.Count > 0 ? string.Join(", ", seriesAudioLanguages) : "none",
                seriesSubtitleLanguages.Count > 0 ? string.Join(", ", seriesSubtitleLanguages) : "none");
        }

        // Save series changes to repository
        await series.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Processes the libraries collections.
    /// </summary>
    /// <param name="fullScan">if set to <c>true</c> [full scan].</param>
    /// <param name="synchronously">if set to <c>true</c> [synchronously].</param>
    /// <param name="subtitleTags">if set to <c>true</c> [extract subtitle languages].</param>
    /// <returns>A <see cref="Task"/> representing the library scan progress.</returns>
    private async Task ProcessLibraryCollections(bool fullScan, bool synchronously, bool subtitleTags)
    {
        LogProcessingHeader("Processing collections...");

        var collections = _queryService.GetBoxSetsFromLibrary();
        var (collectionsProcessed, collectionsSkipped) = await ProcessItemsAsync(
            collections,
            async (collection, ct) => await ProcessCollection(collection, fullScan, subtitleTags, ct).ConfigureAwait(false),
            synchronously).ConfigureAwait(false);

        _logger.LogInformation(
            "COLLECTIONS - processed {Processed} of {Total} ({Skipped} skipped)",
            collectionsProcessed,
            collections.Count,
            collectionsSkipped);
    }

    private async Task<bool> ProcessCollection(BoxSet collection, bool fullRefresh, bool subtitleTags, CancellationToken cancellationToken)
    {
        // Alternative approach using GetLinkedChildren if the above doesn't work:
        var collectionItems = collection.GetLinkedChildren()
            .OfType<Movie>()
            .ToList();

        if (collectionItems.Count == 0)
        {
            _logger.LogWarning("No movies found in box set {BoxSetName}", collection.Name);
            return false;
        }

        // Get language tags from all movies in the box set
        var collectionAudioLanguages = new List<string>();
        var collectionSubtitleLanguages = new List<string>();
        foreach (var movie in collectionItems)
        {
            if (movie == null)
            {
                _logger.LogWarning("Movie is null!");
                continue;
            }

            var movieLanguages = _tagService.GetLanguageTags(movie, TagType.Audio);
            collectionAudioLanguages.AddRange(movieLanguages);

            var movieSubtitleLanguages = _tagService.GetLanguageTags(movie, TagType.Subtitle);
            collectionSubtitleLanguages.AddRange(movieSubtitleLanguages);
        }

        // Strip audio language prefix
        collectionAudioLanguages = _tagService.StripTagPrefix(collectionAudioLanguages, TagType.Audio);

        // Add language tags to the box set
        collectionAudioLanguages = collectionAudioLanguages.Distinct().ToList();
        var addedAudioLanguages = await Task.Run(() => _tagService.AddLanguageTags(collection, collectionAudioLanguages, TagType.Audio, convertFromIso: false), cancellationToken).ConfigureAwait(false);

        // Strip subtitle language prefix
        collectionSubtitleLanguages = _tagService.StripTagPrefix(collectionSubtitleLanguages, TagType.Subtitle);

        // Add subtitle language tags to the box set
        collectionSubtitleLanguages = collectionSubtitleLanguages.Distinct().ToList();
        List<string> addedSubtitleLanguages = new List<string>();
        if (subtitleTags && collectionSubtitleLanguages.Count > 0)
        {
            addedSubtitleLanguages = await Task.Run(() => _tagService.AddLanguageTags(collection, collectionSubtitleLanguages, TagType.Subtitle, convertFromIso: false), cancellationToken).ConfigureAwait(false);
        }

        // Only log if new tags were actually added
        if (addedAudioLanguages.Count > 0 || addedSubtitleLanguages.Count > 0)
        {
            _logger.LogInformation(
                "COLLECTION - {CollectionName} - audio: {Audio} - subtitles: {Subtitles}",
                collection.Name,
                addedAudioLanguages.Count > 0 ? string.Join(", ", addedAudioLanguages) : "none",
                addedSubtitleLanguages.Count > 0 ? string.Join(", ", addedSubtitleLanguages) : "none");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Processes the libraries external subtitles.
    /// </summary>
    /// <param name="synchronously">if set to <c>true</c> [synchronously].</param>
    /// <param name="subtitleTags">if set to <c>true</c> [extract subtitle languages].</param>
    private async Task ProcessLibraryExternalSubtitles(bool synchronously, bool subtitleTags)
    {
        if (!subtitleTags)
        {
            _logger.LogInformation("Skipping external subtitle processing as subtitle tag extraction is disabled");
            return;
        }

        LogProcessingHeader("Processing external subtitles...");

        // Process movies
        var movies = _queryService.GetMoviesFromLibrary();
        var (processedMovies, skippedMovies) = await ProcessItemsAsync(
            movies,
            async (movie, ct) => await ProcessMovieExternalSubtitles(movie, ct).ConfigureAwait(false),
            synchronously).ConfigureAwait(false);
        _logger.LogInformation(
            "EXTERNAL SUBTITLES - MOVIES - processed {Processed} of {Total} ({Skipped} skipped)",
            processedMovies,
            movies.Count,
            skippedMovies);

        // Process collections
        var collections = _queryService.GetBoxSetsFromLibrary();
        var (processedCollections, skippedCollections) = await ProcessItemsAsync(
            collections,
            async (collection, ct) => await ProcessCollectionExternalSubtitles(collection, ct).ConfigureAwait(false),
            synchronously).ConfigureAwait(false);
        _logger.LogInformation(
            "EXTERNAL SUBTITLES - COLLECTIONS - processed {Processed} of {Total} ({Skipped} skipped)",
            processedCollections,
            collections.Count,
            skippedCollections);

        // Process series
        var seriesList = _queryService.GetSeriesFromLibrary();
        var (processedSeries, skippedSeries) = await ProcessItemsAsync(
            seriesList,
            async (seriesBaseItem, ct) =>
            {
                if (seriesBaseItem is Series series)
                {
                    await ProcessSeriesExternalSubtitles(series, ct).ConfigureAwait(false);
                    return true;
                }
                else
                {
                    _logger.LogWarning("Series is null!");
                    return false;
                }
            },
            synchronously).ConfigureAwait(false);
        _logger.LogInformation(
            "EXTERNAL SUBTITLES - SERIES - processed {Processed} of {Total} ({Skipped} skipped)",
            processedSeries,
            seriesList.Count,
            skippedSeries);
    }

    private async Task<bool> ProcessMovieExternalSubtitles(Movie movie, CancellationToken cancellationToken)
    {
        if (movie is not Video video)
        {
            _logger.LogWarning("Movie is null!");
            return false;
        }

        // Check if the video has subtitle language tags from external files
        var subtitleLanguages = _subtitleService.ExtractSubtitleLanguagesExternal(movie);
        if (subtitleLanguages.Count > 0)
        {
            subtitleLanguages = await Task.Run(() => _tagService.AddLanguageTags(video, subtitleLanguages, TagType.Subtitle, convertFromIso: true), cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Added external subtitle tags for VIDEO {VideoName}: {SubtitleLanguages}", video.Name, string.Join(", ", subtitleLanguages));
            return true;
        }
        else
        {
            _logger.LogWarning("No external subtitle information found for VIDEO {VideoName}", video.Name);
            return false;
        }
    }

    private async Task<bool> ProcessCollectionExternalSubtitles(BoxSet collection, CancellationToken cancellationToken)
    {
        var collectionItems = _libraryManager.QueryItems(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Movie],
            Recursive = true,
            ParentId = collection.Id
        }).Items.Select(m => m as Movie).ToList();

        if (collectionItems.Count == 0)
        {
            _logger.LogWarning("No movies found in box set {BoxSetName}", collection.Name);
            return false;
        }

        var collectionSubtitleLanguages = new List<string>();
        foreach (var movie in collectionItems)
        {
            if (movie == null)
            {
                _logger.LogWarning("Movie is null!");
                continue;
            }

            var movieSubtitleLanguages = _subtitleService.ExtractSubtitleLanguagesExternal(movie);
            collectionSubtitleLanguages.AddRange(movieSubtitleLanguages);
        }

        // Add subtitle language tags to the box set
        collectionSubtitleLanguages = collectionSubtitleLanguages.Distinct().ToList();
        if (collectionSubtitleLanguages.Count > 0)
        {
            collectionSubtitleLanguages = await Task.Run(() => _tagService.AddLanguageTags(collection, collectionSubtitleLanguages, TagType.Subtitle, convertFromIso: true), cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Added external subtitle tags for {BoxSetName}: {Languages}", collection.Name, string.Join(", ", collectionSubtitleLanguages));
            return true;
        }
        else
        {
            _logger.LogWarning("No external subtitle information found for {BoxSetName}", collection.Name);
            return false;
        }
    }

    private async Task ProcessSeriesExternalSubtitles(Series series, CancellationToken cancellationToken)
    {
        // Get all seasons in the series
        var seasons = _queryService.GetSeasonsFromSeries(series);
        if (seasons == null || seasons.Count == 0)
        {
            _logger.LogWarning("No seasons found in SERIES {SeriesName}", series.Name);
            return;
        }

        // Get language tags from all seasons in the series
        _logger.LogInformation("Processing SERIES {SeriesName}", series.Name);
        var seriesSubtitleLanguages = new List<string>();
        foreach (var season in seasons)
        {
            var episodes = _queryService.GetEpisodesFromSeason(season);

            if (episodes == null || episodes.Count == 0)
            {
                _logger.LogWarning("No episodes found in SEASON {SeasonName} of {SeriesName}", season.Name, series.Name);
                continue;
            }

            // Get language tags from all episodes in the season
            _logger.LogInformation("Processing SEASON {SeasonName} of {SeriesName}", season.Name, series.Name);
            var seasonSubtitleLanguages = new List<string>();
            foreach (var episode in episodes)
            {
                if (episode is Video video)
                {
                    var subtitleLanguages = _subtitleService.ExtractSubtitleLanguagesExternal(video);
                    seasonSubtitleLanguages.AddRange(subtitleLanguages);

                    if (subtitleLanguages.Count > 0)
                    {
                        _logger.LogInformation("Found external subtitle tags for VIDEO {VideoName}: {SubtitleLanguages}", video.Name, string.Join(", ", subtitleLanguages));
                    }
                    else
                    {
                        _logger.LogInformation("No external subtitle information found for VIDEO {VideoName}", video.Name);
                    }
                }
            }

            // Make sure we have unique language tags
            seasonSubtitleLanguages = seasonSubtitleLanguages.Distinct().ToList();

            // Add the season languages to the series languages
            seriesSubtitleLanguages.AddRange(seasonSubtitleLanguages);

            // Add subtitle language tags to the season
            if (seasonSubtitleLanguages.Count > 0)
            {
                seasonSubtitleLanguages = await Task.Run(() => _tagService.AddLanguageTags(season, seasonSubtitleLanguages, TagType.Subtitle, convertFromIso: true), cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Added external subtitle tags for SEASON {SeasonName} of {SeriesName}: {Languages}", season.Name, series.Name, string.Join(", ", seasonSubtitleLanguages));
            }
            else
            {
                _logger.LogWarning("No external subtitle information found for SEASON {SeasonName} of {SeriesName}", season.Name, series.Name);
            }
        }

        // Make sure we have unique language tags
        seriesSubtitleLanguages = seriesSubtitleLanguages.Distinct().ToList();

        // Add subtitle language tags to the series
        if (seriesSubtitleLanguages.Count > 0)
        {
            seriesSubtitleLanguages = await Task.Run(() => _tagService.AddLanguageTags(series, seriesSubtitleLanguages, TagType.Subtitle, convertFromIso: true), cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Added external subtitle tags for SERIES {SeriesName}: {Languages}", series.Name, string.Join(", ", seriesSubtitleLanguages));
        }
        else
        {
            _logger.LogWarning("No external subtitle information found for SERIES {SeriesName}", series.Name);
        }
    }

    private async Task<(List<string> AudioLanguages, List<string> SubtitleLanguages)> ProcessVideo(Video video, bool subtitleTags, CancellationToken cancellationToken)
    {
        var audioLanguages = new List<string>();
        var subtitleLanguages = new List<string>();

        try
        {
            // Get media sources from the video
            var mediaSources = video.GetMediaSources(false);

            if (mediaSources == null || mediaSources.Count == 0)
            {
                _logger.LogWarning("No media sources found for VIDEO {VideoName}", video.Name);

                // Still try to add undefined tag if no sources found
                audioLanguages = await _tagService.AddAudioLanguageTagsOrUndefined(video, audioLanguages, cancellationToken).ConfigureAwait(false);
                return (audioLanguages, subtitleLanguages);
            }

            foreach (var source in mediaSources)
            {
                if (source.MediaStreams == null || source.MediaStreams.Count == 0)
                {
                    continue;
                }

                // Extract audio languages from audio streams
                var audioStreams = source.MediaStreams
                    .Where(s => s.Type == MediaBrowser.Model.Entities.MediaStreamType.Audio)
                    .ToList();

                foreach (var stream in audioStreams)
                {
                    var langCode = stream.Language;
                    if (!string.IsNullOrEmpty(langCode) &&
                        !langCode.Equals("und", StringComparison.OrdinalIgnoreCase) &&
                        !langCode.Equals("root", StringComparison.OrdinalIgnoreCase))
                    {
                        // Convert 2-letter codes to 3-letter codes
                        var threeLetterCode = _conversionService.ConvertToThreeLetterIsoCode(langCode);
                        audioLanguages.Add(threeLetterCode);
                    }
                }

                // Extract subtitle languages if enabled
                if (subtitleTags)
                {
                    var subtitleStreams = source.MediaStreams
                        .Where(s => s.Type == MediaBrowser.Model.Entities.MediaStreamType.Subtitle)
                        .ToList();

                    foreach (var stream in subtitleStreams)
                    {
                        var langCode = stream.Language;
                        if (!string.IsNullOrEmpty(langCode) &&
                            !langCode.Equals("und", StringComparison.OrdinalIgnoreCase) &&
                            !langCode.Equals("root", StringComparison.OrdinalIgnoreCase))
                        {
                            // Convert 2-letter codes to 3-letter codes
                            var threeLetterCode = _conversionService.ConvertToThreeLetterIsoCode(langCode);
                            subtitleLanguages.Add(threeLetterCode);
                        }
                    }
                }
            }

            // Get external subtitle files as well
            if (subtitleTags)
            {
                var externalSubtitles = _subtitleService.ExtractSubtitleLanguagesExternal(video);
                subtitleLanguages.AddRange(externalSubtitles);
            }

            // Remove duplicates
            audioLanguages = audioLanguages.Distinct().ToList();
            subtitleLanguages = subtitleLanguages.Distinct().ToList();

            if (audioLanguages.Count > 0)
            {
                // Add audio language tags
                audioLanguages = await Task.Run(() => _tagService.AddLanguageTags(video, audioLanguages, TagType.Audio, convertFromIso: true), cancellationToken).ConfigureAwait(false);
                _logger.LogDebug("Added audio tags for VIDEO {VideoName}: {AudioLanguages}", video.Name, string.Join(", ", audioLanguages));
            }
            else
            {
                audioLanguages = await _tagService.AddAudioLanguageTagsOrUndefined(video, audioLanguages, cancellationToken).ConfigureAwait(false);
            }

            if (subtitleTags && subtitleLanguages.Count > 0)
            {
                // Add subtitle language tags
                subtitleLanguages = await Task.Run(() => _tagService.AddLanguageTags(video, subtitleLanguages, TagType.Subtitle, convertFromIso: true), cancellationToken).ConfigureAwait(false);
                _logger.LogDebug("Added subtitle tags for VIDEO {VideoName}: {SubtitleLanguages}", video.Name, string.Join(", ", subtitleLanguages));
            }
            else if (subtitleTags)
            {
                _logger.LogWarning("No subtitle information found for VIDEO {VideoName}", video.Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing {VideoName}", video.Name);
        }

        return (audioLanguages, subtitleLanguages);
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // _libraryManager.ItemUpdated += OnLibraryManagerItemUpdated;

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        // _libraryManager.ItemUpdated -= OnLibraryManagerItemUpdated;

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases unmanaged and - optionally - managed resources.
    /// </summary>
    /// <param name="dispose"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
    protected virtual void Dispose(bool dispose)
    {
        if (dispose)
        {
            // _timer.Dispose();
        }
    }
}
