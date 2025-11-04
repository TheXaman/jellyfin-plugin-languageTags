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
    /// <param name="processor">Function to process each item.</param>
    /// <param name="synchronously">If true, process items synchronously; if false, process in parallel.</param>
    /// <returns>Number of processed items.</returns>
    private async Task<int> ProcessItemsAsync<T>(
        List<T> items,
        Func<T, CancellationToken, Task> processor,
        bool synchronously)
    {
        int processed = 0;

        if (!synchronously)
        {
            await Parallel.ForEachAsync(items, async (item, ct) =>
            {
                await processor(item, ct).ConfigureAwait(false);
                Interlocked.Increment(ref processed);
            }).ConfigureAwait(false);
        }
        else
        {
            foreach (var item in items)
            {
                await processor(item, CancellationToken.None).ConfigureAwait(false);
                Interlocked.Increment(ref processed);
            }
        }

        return processed;
    }

    /// <summary>
    /// Scans the library.
    /// </summary>
    /// <param name="fullScan">if set to <c>true</c> [full scan].</param>
    /// <param name="type">The type of refresh to perform. Default is "everything".</param>
    /// <returns>A <see cref="Task"/> representing the library scan progress.</returns>
    public async Task ScanLibrary(bool fullScan = false, string type = "everything")
    {
        // Get configuration value for AlwaysForceFullRefresh
        var alwaysForceFullRefresh = _configService.AlwaysForceFullRefresh;
        fullScan = fullScan || alwaysForceFullRefresh;
        if (fullScan)
        {
            _logger.LogInformation("Full scan enabled");
        }

        // Get configuration value for SynchronousRefresh
        var synchronously = _configService.SynchronousRefresh;
        if (synchronously)
        {
            _logger.LogInformation("Synchronous refresh enabled");
        }

        // Get configuration value for AddSubtitleTags
        var subtitleTags = _configService.AddSubtitleTags;
        if (subtitleTags)
        {
            _logger.LogInformation("Extract subtitle languages enabled");
        }

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
                // Process external subtitles
                await ProcessLibraryExternalSubtitles(synchronously, subtitleTags).ConfigureAwait(false);
                break;
            default:
                // Process movies
                await ProcessLibraryMovies(fullScan, synchronously, subtitleTags).ConfigureAwait(false);

                // Process series
                await ProcessLibrarySeries(fullScan, synchronously, subtitleTags).ConfigureAwait(false);

                // Process box sets / collections
                await ProcessLibraryCollections(fullScan, synchronously, subtitleTags).ConfigureAwait(false);

                // Process external subtitles
                await ProcessLibraryExternalSubtitles(synchronously, subtitleTags).ConfigureAwait(false);

                // Process non-media items
                await ProcessNonMediaItems().ConfigureAwait(false);

                break;
        }
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
            await RemoveLanguageTagsFromItemType(BaseItemKind.Movie, "movies").ConfigureAwait(false);
            await RemoveLanguageTagsFromItemType(BaseItemKind.Episode, "episodes").ConfigureAwait(false);
            await RemoveLanguageTagsFromItemType(BaseItemKind.Season, "seasons").ConfigureAwait(false);
            await RemoveLanguageTagsFromItemType(BaseItemKind.Series, "series").ConfigureAwait(false);
            await RemoveLanguageTagsFromItemType(BaseItemKind.BoxSet, "collections").ConfigureAwait(false);

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
            _tagService.RemoveAudioLanguageTags(item);
            _tagService.RemoveSubtitleLanguageTags(item);
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
        var itemTypesString = _configService.NonMediaItemTypes;
        var itemTypes = itemTypesString.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();

        if (itemTypes.Count == 0)
        {
            _logger.LogInformation("No non-media item types selected for tagging");
            return;
        }

        _logger.LogInformation("************************************");
        _logger.LogInformation("*  Processing non-media items...   *");
        _logger.LogInformation("************************************");
        _logger.LogInformation("Applying tag '{TagName}' to {Count} item types", tagName, itemTypes.Count);

        foreach (var itemType in itemTypes)
        {
            try
            {
                // Convert string to BaseItemKind
                if (!Enum.TryParse<BaseItemKind>(itemType, out var kind))
                {
                    _logger.LogWarning("Unknown item type: {ItemType}", itemType);
                    continue;
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

        _logger.LogInformation("Completed non-media item tagging");
    }

    /// <summary>
    /// Removes non-media tags from all items.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the removal process.</returns>
    public async Task RemoveNonMediaTags()
    {
        var tagName = _configService.NonMediaTag;
        var itemTypesString = _configService.NonMediaItemTypes;
        var itemTypes = itemTypesString.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();

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
                if (Enum.TryParse<BaseItemKind>(itemType, out var kind))
                {
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
    /// Processes the libraries movies.
    /// </summary>
    /// <param name="fullScan">if set to <c>true</c> [full scan].</param>
    /// <param name="synchronously">if set to <c>true</c> [synchronously].</param>
    /// <param name="subtitleTags">if set to <c>true</c> [extract subtitle languages].</param>
    /// <returns>A <see cref="Task"/> representing the library scan progress.</returns>
    private async Task ProcessLibraryMovies(bool fullScan, bool synchronously, bool subtitleTags)
    {
        _logger.LogInformation("****************************");
        _logger.LogInformation("*    Processing movies...  *");
        _logger.LogInformation("****************************");

        // Fetch all movies in the library
        var movies = _queryService.GetMoviesFromLibrary();
        int totalMovies = movies.Count;

        int processedMovies = await ProcessItemsAsync(
            movies,
            async (movie, ct) => await ProcessMovie(movie, fullScan, subtitleTags, ct).ConfigureAwait(false),
            synchronously).ConfigureAwait(false);

        _logger.LogInformation("Processed {ProcessedMovies} of {TotalMovies} movies", processedMovies, totalMovies);
    }

    private async Task ProcessMovie(Movie movie, bool fullScan, bool subtitleTags, CancellationToken cancellationToken)
    {
        if (movie is Video video)
        {
            bool shouldProcessVideo = fullScan;
            bool hasExistingAudioTags = _tagService.HasAudioLanguageTags(video);
            bool hasExistingSubtitleTags = _tagService.HasSubtitleLanguageTags(video);

            // Check if we need to process audio tags
            if (hasExistingAudioTags)
            {
                if (!fullScan)
                {
                    _logger.LogDebug("Audio tags exist for {VideoName}", video.Name);
                }
                else
                {
                    _tagService.RemoveAudioLanguageTags(video);
                    shouldProcessVideo = true;
                }
            }
            else
            {
                // No audio tags exist, need to process
                shouldProcessVideo = true;
            }

            // Check if we need to process subtitle tags
            if (subtitleTags)
            {
                if (hasExistingSubtitleTags)
                {
                    if (fullScan)
                    {
                        _tagService.RemoveSubtitleLanguageTags(video);
                        shouldProcessVideo = true;
                    }
                }
                else
                {
                    // No subtitle tags exist, need to process
                    shouldProcessVideo = true;
                }
            }

            // Process the video if needed
            if (shouldProcessVideo)
            {
                await ProcessVideo(video, subtitleTags, cancellationToken).ConfigureAwait(false);
            }
        }
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
        _logger.LogInformation("****************************");
        _logger.LogInformation("*    Processing series...  *");
        _logger.LogInformation("****************************");

        // Fetch all series in the library
        var seriesList = _queryService.GetSeriesFromLibrary();
        var totalSeries = seriesList.Count;

        var processedSeries = await ProcessItemsAsync(
            seriesList,
            async (seriesBaseItem, ct) =>
            {
                // Check if the series is a valid series
                var series = seriesBaseItem as Series;
                if (series == null)
                {
                    _logger.LogWarning("Series is null!");
                    return;
                }

                await ProcessSeries(series, fullScan, subtitleTags, ct).ConfigureAwait(false);
            },
            synchronously).ConfigureAwait(false);

        _logger.LogInformation("Processed {ProcessedSeries} of {TotalSeries} series", processedSeries, totalSeries);
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
                    bool hasExistingAudioTags = _tagService.HasAudioLanguageTags(video);
                    bool hasExistingSubtitleTags = _tagService.HasSubtitleLanguageTags(video);

                    // Check if the video has subtitle language tags and subtitleTags is enabled
                    if (hasExistingSubtitleTags && subtitleTags)
                    {
                        if (!fullScan)
                        {
                            _logger.LogDebug("Subtitle tags exist for {VideoName}", video.Name);
                            var episodeLanguageNames = _tagService.StripSubtitleTagPrefix(_tagService.GetSubtitleLanguageTags(video));
                            // Convert language names back to ISO codes for consistent processing
                            var episodeLanguagesTmp = _conversionService.ConvertLanguageNamesToIso(episodeLanguageNames);
                            seasonSubtitleLanguages.AddRange(episodeLanguagesTmp);
                        }
                        else
                        {
                            _tagService.RemoveSubtitleLanguageTags(episode);
                            shouldProcessVideo = true;
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
                            var episodeLanguageNames = _tagService.StripAudioTagPrefix(_tagService.GetAudioLanguageTags(video));
                            // Convert language names back to ISO codes for consistent processing
                            var episodeLanguagesTmp = _conversionService.ConvertLanguageNamesToIso(episodeLanguageNames);
                            seasonAudioLanguages.AddRange(episodeLanguagesTmp);
                        }
                        else
                        {
                            _tagService.RemoveAudioLanguageTags(episode);
                            shouldProcessVideo = true;
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

            // Log season processing summary
            if (episodesProcessed > 0 || episodesSkipped > 0)
            {
                _logger.LogInformation(
                    "SEASON {SeasonName} of {SeriesName}: processed {ProcessedCount}/{TotalCount} episodes ({SkippedCount} skipped)",
                    season.Name,
                    series.Name,
                    episodesProcessed,
                    episodes.Count,
                    episodesSkipped);
            }

            // Make sure we have unique language tags
            seasonAudioLanguages = seasonAudioLanguages.Distinct().ToList();
            seasonSubtitleLanguages = seasonSubtitleLanguages.Distinct().ToList();

            // Add the season languages to the series languages
            seriesAudioLanguages.AddRange(seasonAudioLanguages);
            seriesSubtitleLanguages.AddRange(seasonSubtitleLanguages);

            // Remove existing language tags
            _tagService.RemoveAudioLanguageTags(season);
            _tagService.RemoveSubtitleLanguageTags(season);

            // Add audio language tags to the season
            seasonAudioLanguages = await _tagService.AddAudioLanguageTagsOrUndefined(season, seasonAudioLanguages, cancellationToken).ConfigureAwait(false);
            if (seasonAudioLanguages.Count > 0)
            {
                _logger.LogInformation("Added audio tags for SEASON {SeasonName} of {SeriesName}: {Languages}", season.Name, series.Name, string.Join(", ", seasonAudioLanguages));
            }

            // Add subtitle language tags to the season
            if (seasonSubtitleLanguages.Count > 0 && subtitleTags)
            {
                seasonSubtitleLanguages = await Task.Run(() => _tagService.AddSubtitleLanguageTags(season, seasonSubtitleLanguages), cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Added subtitle tags for SEASON {SeasonName} of {SeriesName}: {Languages}", season.Name, series.Name, string.Join(", ", seasonSubtitleLanguages));
            }
            else if (subtitleTags)
            {
                _logger.LogWarning("No subtitle information found for SEASON {SeasonName} of {SeriesName}", season.Name, series.Name);
            }

            // Save season changes to repository
            await season.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
        }

        // Remove existing language tags
        _tagService.RemoveAudioLanguageTags(series);
        _tagService.RemoveSubtitleLanguageTags(series);

        // Make sure we have unique language tags
        seriesAudioLanguages = seriesAudioLanguages.Distinct().ToList();
        seriesSubtitleLanguages = seriesSubtitleLanguages.Distinct().ToList();

        // Add audio language tags to the series
        seriesAudioLanguages = await _tagService.AddAudioLanguageTagsOrUndefined(series, seriesAudioLanguages, cancellationToken).ConfigureAwait(false);
        if (seriesAudioLanguages.Count > 0)
        {
            _logger.LogInformation("Added audio tags for SERIES {SeriesName}: {Languages}", series.Name, string.Join(", ", seriesAudioLanguages));
        }

        // Add subtitle language tags to the series
        if (seriesSubtitleLanguages.Count > 0 && subtitleTags)
        {
            seriesSubtitleLanguages = await Task.Run(() => _tagService.AddSubtitleLanguageTags(series, seriesSubtitleLanguages), cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Added subtitle tags for SERIES {SeriesName}: {Languages}", series.Name, string.Join(", ", seriesSubtitleLanguages));
        }
        else if (subtitleTags)
        {
            _logger.LogWarning("No subtitle information found for SERIES {SeriesName}", series.Name);
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
        _logger.LogInformation("******************************");
        _logger.LogInformation("*  Processing collections... *");
        _logger.LogInformation("******************************");

        // Fetch all box sets in the library
        var collections = _queryService.GetBoxSetsFromLibrary();
        int totalCollections = collections.Count;

        await ProcessItemsAsync(
            collections,
            async (collection, ct) => await ProcessCollection(collection, fullScan, subtitleTags, ct).ConfigureAwait(false),
            synchronously).ConfigureAwait(false);

        _logger.LogInformation("Processed {TotalCollections} collections", totalCollections);
    }

    private async Task ProcessCollection(BoxSet collection, bool fullRefresh, bool subtitleTags, CancellationToken cancellationToken)
    {
        // Alternative approach using GetLinkedChildren if the above doesn't work:
        var collectionItems = collection.GetLinkedChildren()
            .OfType<Movie>()
            .ToList();

        if (collectionItems.Count == 0)
        {
            _logger.LogWarning("No movies found in box set {BoxSetName}", collection.Name);
            return;
        }

        // Remove existing language tags
        _tagService.RemoveAudioLanguageTags(collection);
        _tagService.RemoveSubtitleLanguageTags(collection);

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

            var movieLanguages = _tagService.GetAudioLanguageTags(movie);
            collectionAudioLanguages.AddRange(movieLanguages);

            var movieSubtitleLanguages = _tagService.GetSubtitleLanguageTags(movie);
            collectionSubtitleLanguages.AddRange(movieSubtitleLanguages);
        }

        // Strip audio language prefix
        collectionAudioLanguages = _tagService.StripAudioTagPrefix(collectionAudioLanguages);

        // Add language tags to the box set
        collectionAudioLanguages = collectionAudioLanguages.Distinct().ToList();
        collectionAudioLanguages = await Task.Run(() => _tagService.AddAudioLanguageTagsByName(collection, collectionAudioLanguages), cancellationToken).ConfigureAwait(false);
        if (collectionAudioLanguages.Count > 0)
        {
            _logger.LogInformation("Added audio tags for {BoxSetName}: {Languages}", collection.Name, string.Join(", ", collectionAudioLanguages));
        }

        if (!subtitleTags) // skip subtitle tags
        {
            return;
        }

        // Strip subtitle language prefix
        collectionSubtitleLanguages = _tagService.StripSubtitleTagPrefix(collectionSubtitleLanguages);

        // Add subtitle language tags to the box set
        collectionSubtitleLanguages = collectionSubtitleLanguages.Distinct().ToList();
        if (collectionSubtitleLanguages.Count > 0)
        {
            collectionSubtitleLanguages = await Task.Run(() => _tagService.AddSubtitleLanguageTagsByName(collection, collectionSubtitleLanguages), cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Added subtitle tags for {BoxSetName}: {Languages}", collection.Name, string.Join(", ", collectionSubtitleLanguages));
        }
        else
        {
            _logger.LogWarning("No subtitle information found for {BoxSetName}", collection.Name);
        }
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

        _logger.LogInformation("**************************************");
        _logger.LogInformation("*  Processing external subtitles...  *");
        _logger.LogInformation("**************************************");

        // Fetch all movies in the library
        var movies = _queryService.GetMoviesFromLibrary();
        int totalMovies = movies.Count;

        int processedMovies = await ProcessItemsAsync(
            movies,
            async (movie, ct) => await ProcessMovieExternalSubtitles(movie, ct).ConfigureAwait(false),
            synchronously).ConfigureAwait(false);

        _logger.LogInformation("Processed {ProcessedMovies} of {TotalMovies} movies", processedMovies, totalMovies);

        // Fetch all box sets in the library
        var collections = _queryService.GetBoxSetsFromLibrary();
        int totalCollections = collections.Count;

        await ProcessItemsAsync(
            collections,
            async (collection, ct) => await ProcessCollectionExternalSubtitles(collection, ct).ConfigureAwait(false),
            synchronously).ConfigureAwait(false);

        _logger.LogInformation("Processed {TotalCollections} collections", totalCollections);

        // Fetch all series in the library
        var seriesList = _queryService.GetSeriesFromLibrary();
        var totalSeries = seriesList.Count;

        var processedSeries = await ProcessItemsAsync(
            seriesList,
            async (seriesBaseItem, ct) =>
            {
                // Check if the series is a valid series
                var series = seriesBaseItem as Series;
                if (series == null)
                {
                    _logger.LogWarning("Series is null!");
                    return;
                }

                await ProcessSeriesExternalSubtitles(series, ct).ConfigureAwait(false);
            },
            synchronously).ConfigureAwait(false);

        _logger.LogInformation("Processed {ProcessedSeries} of {TotalSeries} series", processedSeries, totalSeries);
    }

    private async Task ProcessMovieExternalSubtitles(Movie movie, CancellationToken cancellationToken)
    {
        if (movie is not Video video)
        {
            _logger.LogWarning("Movie is null!");
            return;
        }

        // Check if the video has subtitle language tags from external files
        var subtitleLanguages = _subtitleService.ExtractSubtitleLanguagesExternal(movie);
        if (subtitleLanguages.Count > 0)
        {
            subtitleLanguages = await Task.Run(() => _tagService.AddSubtitleLanguageTags(video, subtitleLanguages), cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Added external subtitle tags for VIDEO {VideoName}: {SubtitleLanguages}", video.Name, string.Join(", ", subtitleLanguages));
        }
        else
        {
            _logger.LogWarning("No external subtitle information found for VIDEO {VideoName}", video.Name);
        }
    }

    private async Task ProcessCollectionExternalSubtitles(BoxSet collection, CancellationToken cancellationToken)
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
            return;
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
            collectionSubtitleLanguages = await Task.Run(() => _tagService.AddSubtitleLanguageTags(collection, collectionSubtitleLanguages), cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Added external subtitle tags for {BoxSetName}: {Languages}", collection.Name, string.Join(", ", collectionSubtitleLanguages));
        }
        else
        {
            _logger.LogWarning("No external subtitle information found for {BoxSetName}", collection.Name);
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
                seasonSubtitleLanguages = await Task.Run(() => _tagService.AddSubtitleLanguageTags(season, seasonSubtitleLanguages), cancellationToken).ConfigureAwait(false);
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
            seriesSubtitleLanguages = await Task.Run(() => _tagService.AddSubtitleLanguageTags(series, seriesSubtitleLanguages), cancellationToken).ConfigureAwait(false);
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
                audioLanguages = await Task.Run(() => _tagService.AddAudioLanguageTags(video, audioLanguages), cancellationToken).ConfigureAwait(false);
                _logger.LogDebug("Added audio tags for VIDEO {VideoName}: {AudioLanguages}", video.Name, string.Join(", ", audioLanguages));
            }
            else
            {
                audioLanguages = await _tagService.AddAudioLanguageTagsOrUndefined(video, audioLanguages, cancellationToken).ConfigureAwait(false);
            }

            if (subtitleTags && subtitleLanguages.Count > 0)
            {
                // Add subtitle language tags
                subtitleLanguages = await Task.Run(() => _tagService.AddSubtitleLanguageTags(video, subtitleLanguages), cancellationToken).ConfigureAwait(false);
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
