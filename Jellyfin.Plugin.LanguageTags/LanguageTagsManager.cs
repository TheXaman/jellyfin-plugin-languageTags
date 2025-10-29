using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Extensions;
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
    private static readonly char[] Separator = new[] { ',' };

    /// <summary>
    /// Initializes a new instance of the <see cref="LanguageTagsManager"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="collectionManager">Instance of the <see cref="ICollectionManager"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{LanguageTagsManager}"/> interface.</param>
    public LanguageTagsManager(ILibraryManager libraryManager, ICollectionManager collectionManager, ILogger<LanguageTagsManager> logger)
    {
        _libraryManager = libraryManager;
        _collectionManager = collectionManager;
        _logger = logger;
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
        var alwaysForceFullRefresh = Plugin.Instance?.Configuration?.AlwaysForceFullRefresh ?? false;
        fullScan = fullScan || alwaysForceFullRefresh;
        if (fullScan)
        {
            _logger.LogInformation("Full scan enabled");
        }

        // Get configuration value for SynchronousRefresh
        var synchronously = Plugin.Instance?.Configuration?.SynchronousRefresh ?? false;
        if (synchronously)
        {
            _logger.LogInformation("Synchronous refresh enabled");
        }

        // Get configuration value for AddSubtitleTags
        var subtitleTags = Plugin.Instance?.Configuration?.AddSubtitleTags ?? false;
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
            // Remove from all movies
            var movies = _libraryManager.QueryItems(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie },
                Recursive = true
            }).Items;

            _logger.LogInformation("Removing language tags from {Count} movies", movies.Count);
            foreach (var movie in movies)
            {
                RemoveAudioLanguageTags(movie);
                RemoveSubtitleLanguageTags(movie);
                await movie.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);
            }

            // Remove from all episodes
            var episodes = _libraryManager.QueryItems(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Episode },
                Recursive = true
            }).Items;

            _logger.LogInformation("Removing language tags from {Count} episodes", episodes.Count);
            foreach (var episode in episodes)
            {
                RemoveAudioLanguageTags(episode);
                RemoveSubtitleLanguageTags(episode);
                await episode.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);
            }

            // Remove from all seasons
            var seasons = _libraryManager.QueryItems(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Season },
                Recursive = true
            }).Items;

            _logger.LogInformation("Removing language tags from {Count} seasons", seasons.Count);
            foreach (var season in seasons)
            {
                RemoveAudioLanguageTags(season);
                RemoveSubtitleLanguageTags(season);
                await season.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);
            }

            // Remove from all series
            var series = _libraryManager.QueryItems(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Series },
                Recursive = true
            }).Items;

            _logger.LogInformation("Removing language tags from {Count} series", series.Count);
            foreach (var show in series)
            {
                RemoveAudioLanguageTags(show);
                RemoveSubtitleLanguageTags(show);
                await show.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);
            }

            // Remove from all collections/box sets
            var collections = _libraryManager.QueryItems(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.BoxSet },
                Recursive = true
            }).Items;

            _logger.LogInformation("Removing language tags from {Count} collections", collections.Count);
            foreach (var collection in collections)
            {
                RemoveAudioLanguageTags(collection);
                RemoveSubtitleLanguageTags(collection);
                await collection.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None).ConfigureAwait(false);
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
    /// Processes non-media items and applies tags.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the processing.</returns>
    public async Task ProcessNonMediaItems()
    {
        var config = Plugin.Instance?.Configuration;
        if (config == null || !config.EnableNonMediaTagging)
        {
            _logger.LogDebug("Non-media tagging is disabled");
            return;
        }

        var tagName = config.NonMediaTag ?? "item";
        var itemTypesString = config.NonMediaItemTypes ?? string.Empty;
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
        var config = Plugin.Instance?.Configuration;
        var tagName = config?.NonMediaTag ?? "item";
        var itemTypesString = config?.NonMediaItemTypes ?? string.Empty;
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
        var movies = GetMoviesFromLibrary();
        int totalMovies = movies.Count;
        int processedMovies = 0;

        if (!synchronously)
        {
            await Parallel.ForEachAsync(movies, async (item, cancellationToken) =>
            {
                await ProcessMovie(item, fullScan, subtitleTags, cancellationToken).ConfigureAwait(false);

                Interlocked.Increment(ref processedMovies);
            }).ConfigureAwait(false);
        }
        else
        {
            foreach (var movie in movies)
            {
                await ProcessMovie(movie, fullScan, subtitleTags, CancellationToken.None).ConfigureAwait(false);

                Interlocked.Increment(ref processedMovies);
            }
        }

        _logger.LogInformation("Processed {ProcessedMovies} of {TotalMovies} movies", processedMovies, totalMovies);
    }

    private async Task ProcessMovie(Movie movie, bool fullScan, bool subtitleTags, CancellationToken cancellationToken)
    {
        if (movie is Video video)
        {
            if (HasAudioLanguageTags(video))
            {
                if (!fullScan)
                {
                    _logger.LogInformation("Audio tags exist, skipping {VideoName}", video.Name);
                    return;
                }

                RemoveAudioLanguageTags(video);
            }

            await ProcessVideo(video, subtitleTags, cancellationToken).ConfigureAwait(false);
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
        var seriesList = GetSeriesFromLibrary();
        var totalSeries = seriesList.Count;
        var processedSeries = 0;

        if (!synchronously)
        {
            await Parallel.ForEachAsync(seriesList, async (seriesBaseItem, cancellationToken) =>
            {
                // Check if the series is a valid series
                var series = seriesBaseItem as Series;
                if (series == null)
                {
                    _logger.LogWarning("Series is null!");
                    return;
                }

                await ProcessSeries(series, fullScan, subtitleTags, cancellationToken).ConfigureAwait(false);
                Interlocked.Increment(ref processedSeries);
            }).ConfigureAwait(false);
        }
        else
        {
            foreach (var seriesBaseItem in seriesList)
            {
                // Check if the series is a valid series
                var series = seriesBaseItem as Series;
                if (series == null)
                {
                    _logger.LogWarning("Series is null!");
                    continue;
                }

                await ProcessSeries(series, fullScan, subtitleTags, CancellationToken.None).ConfigureAwait(false);
                Interlocked.Increment(ref processedSeries);
            }
        }

        _logger.LogInformation("Processed {ProcessedSeries} of {TotalSeries} series", processedSeries, totalSeries);
    }

    private async Task ProcessSeries(Series series, bool fullScan, bool subtitleTags, CancellationToken cancellationToken)
    {
        // Get all seasons in the series
        var seasons = GetSeasonsFromSeries(series);
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
            var episodes = GetEpisodesFromSeason(season);

            if (episodes == null || episodes.Count == 0)
            {
                _logger.LogWarning("No episodes found in SEASON {SeasonName}", season.Name);
                continue;
            }

            // Get language tags from all episodes in the season
            _logger.LogInformation("Processing SEASON {SeasonName} of {SeriesName}", season.Name, series.Name);
            var seasonAudioLanguages = new List<string>();
            var seasonSubtitleLanguages = new List<string>();
            foreach (var episode in episodes)
            {
                if (episode is Video video)
                {
                    // Check if the video has subtitle language tags and subtitleTags is enabled
                    if (HasSubtitleLanguageTags(video) && subtitleTags)
                    {
                        if (!fullScan)
                        {
                            _logger.LogInformation("Subtitle tags exist for {VideoName}", video.Name);
                            var episodeLanguagesTmp = StripSubtitleTagPrefix(GetSubtitleLanguageTags(video));
                            seasonSubtitleLanguages.AddRange(episodeLanguagesTmp);
                        }
                        else
                        {
                            RemoveSubtitleLanguageTags(episode);
                        }
                    }

                    // Check if the video has audio language tags
                    if (HasAudioLanguageTags(video))
                    {
                        if (!fullScan)
                        {
                            _logger.LogInformation("Audio tags exist, skipping {VideoName}", video.Name);
                            var episodeLanguagesTmp = StripAudioTagPrefix(GetAudioLanguageTags(video));
                            seasonAudioLanguages.AddRange(episodeLanguagesTmp);
                            continue;
                        }
                        else
                        {
                            RemoveAudioLanguageTags(episode);
                        }
                    }

                    var (audioLanguages, subtitleLanguages) = await ProcessVideo(video, subtitleTags, cancellationToken).ConfigureAwait(false);
                    seasonAudioLanguages.AddRange(audioLanguages);
                    seasonSubtitleLanguages.AddRange(subtitleLanguages);
                }
            }

            // Make sure we have unique language tags
            seasonAudioLanguages = seasonAudioLanguages.Distinct().ToList();
            seasonSubtitleLanguages = seasonSubtitleLanguages.Distinct().ToList();

            // Add the season languages to the series languages
            seriesAudioLanguages.AddRange(seasonAudioLanguages);
            seriesSubtitleLanguages.AddRange(seasonSubtitleLanguages);

            // Remove existing language tags
            RemoveAudioLanguageTags(season);
            RemoveSubtitleLanguageTags(season);

            // Add audio language tags to the season
            seasonAudioLanguages = await AddAudioLanguageTagsOrUndefined(season, seasonAudioLanguages, cancellationToken).ConfigureAwait(false);
            if (seasonAudioLanguages.Count > 0)
            {
                _logger.LogInformation("Added audio tags for SEASON {SeasonName} of {SeriesName}: {Languages}", season.Name, series.Name, string.Join(", ", seasonAudioLanguages));
            }

            // Add subtitle language tags to the season
            if (seasonSubtitleLanguages.Count > 0 && subtitleTags)
            {
                seasonSubtitleLanguages = await Task.Run(() => AddSubtitleLanguageTags(season, seasonSubtitleLanguages), cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Added subtitle tags for SEASON {SeasonName} of {SeriesName}: {Languages}", season.Name, series.Name, string.Join(", ", seasonSubtitleLanguages));
            }
            else if (subtitleTags)
            {
                _logger.LogWarning("No subtitle information found for SEASON {SeasonName} of {SeriesName}", season.Name, series.Name);
            }
        }

        // Remove existing language tags
        RemoveAudioLanguageTags(series);
        RemoveSubtitleLanguageTags(series);

        // Make sure we have unique language tags
        seriesAudioLanguages = seriesAudioLanguages.Distinct().ToList();
        seriesSubtitleLanguages = seriesSubtitleLanguages.Distinct().ToList();

        // Add audio language tags to the series
        seriesAudioLanguages = await AddAudioLanguageTagsOrUndefined(series, seriesAudioLanguages, cancellationToken).ConfigureAwait(false);
        if (seriesAudioLanguages.Count > 0)
        {
            _logger.LogInformation("Added audio tags for SERIES {SeriesName}: {Languages}", series.Name, string.Join(", ", seriesAudioLanguages));
        }

        // Add subtitle language tags to the series
        if (seriesSubtitleLanguages.Count > 0 && subtitleTags)
        {
            seriesSubtitleLanguages = await Task.Run(() => AddSubtitleLanguageTags(series, seriesSubtitleLanguages), cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Added subtitle tags for SERIES {SeriesName}: {Languages}", series.Name, string.Join(", ", seriesSubtitleLanguages));
        }
        else if (subtitleTags)
        {
            _logger.LogWarning("No subtitle information found for SERIES {SeriesName}", series.Name);
        }
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
        var collections = GetBoxSetsFromLibrary();
        int totalCollections = collections.Count;

        if (!synchronously)
        {
            await Parallel.ForEachAsync(collections, async (item, cancellationToken) =>
            {
                await ProcessCollection(item, fullScan, subtitleTags, cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        else
        {
            foreach (var collection in collections)
            {
                await ProcessCollection(collection, fullScan, subtitleTags, CancellationToken.None).ConfigureAwait(false);
            }
        }

        _logger.LogInformation("Processed {TotalCollections} collections", totalCollections);
    }

    private async Task ProcessCollection(BoxSet collection, bool fullRefresh, bool subtitleTags, CancellationToken cancellationToken)
    {
        if (collection is BoxSet boxSet)
        {
            // Alternative approach using GetLinkedChildren if the above doesn't work:
            var collectionItems = boxSet.GetLinkedChildren()
                .OfType<Movie>()
                .ToList();

            if (collectionItems.Count == 0)
            {
                _logger.LogWarning("No movies found in box set {BoxSetName}", boxSet.Name);
                return;
            }

            // Remove existing language tags
            RemoveAudioLanguageTags(collection);
            RemoveSubtitleLanguageTags(collection);

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

                var movieLanguages = GetAudioLanguageTags(movie);
                collectionAudioLanguages.AddRange(movieLanguages);

                var movieSubtitleLanguages = GetSubtitleLanguageTags(movie);
                collectionSubtitleLanguages.AddRange(movieSubtitleLanguages);
            }

            // Strip audio language prefix
            collectionAudioLanguages = StripAudioTagPrefix(collectionAudioLanguages);

            // Add language tags to the box set
            collectionAudioLanguages = collectionAudioLanguages.Distinct().ToList();
            collectionAudioLanguages = await Task.Run(() => AddAudioLanguageTagsByName(boxSet, collectionAudioLanguages), cancellationToken).ConfigureAwait(false);
            if (collectionAudioLanguages.Count > 0)
            {
                _logger.LogInformation("Added audio tags for {BoxSetName}: {Languages}", boxSet.Name, string.Join(", ", collectionAudioLanguages));
            }

            if (!subtitleTags) // skip subtitle tags
            {
                return;
            }

            // Strip subtitle language prefix
            collectionSubtitleLanguages = StripSubtitleTagPrefix(collectionSubtitleLanguages);

            // Add subtitle language tags to the box set
            collectionSubtitleLanguages = collectionSubtitleLanguages.Distinct().ToList();
            if (collectionSubtitleLanguages.Count > 0)
            {
                collectionSubtitleLanguages = await Task.Run(() => AddSubtitleLanguageTagsByName(boxSet, collectionSubtitleLanguages), cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Added subtitle tags for {BoxSetName}: {Languages}", boxSet.Name, string.Join(", ", collectionSubtitleLanguages));
            }
            else
            {
                _logger.LogWarning("No subtitle information found for {BoxSetName}", boxSet.Name);
            }
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
        var movies = GetMoviesFromLibrary();
        int totalMovies = movies.Count;
        int processedMovies = 0;

        if (!synchronously)
        {
            await Parallel.ForEachAsync(movies, async (item, cancellationToken) =>
            {
                await ProcessMovieExternalSubtitles(item, cancellationToken).ConfigureAwait(false);

                Interlocked.Increment(ref processedMovies);
            }).ConfigureAwait(false);
        }
        else
        {
            foreach (var movie in movies)
            {
                await ProcessMovieExternalSubtitles(movie, CancellationToken.None).ConfigureAwait(false);

                Interlocked.Increment(ref processedMovies);
            }
        }

        _logger.LogInformation("Processed {ProcessedMovies} of {TotalMovies} movies", processedMovies, totalMovies);

        // Fetch all box sets in the library
        var collections = GetBoxSetsFromLibrary();
        int totalCollections = collections.Count;

        if (!synchronously)
        {
            await Parallel.ForEachAsync(collections, async (item, cancellationToken) =>
            {
                await ProcessCollectionExternalSubtitles(item, cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        else
        {
            foreach (var collection in collections)
            {
                await ProcessCollectionExternalSubtitles(collection, CancellationToken.None).ConfigureAwait(false);
            }
        }

        _logger.LogInformation("Processed {TotalCollections} collections", totalCollections);

        // Fetch all series in the library
        var seriesList = GetSeriesFromLibrary();
        var totalSeries = seriesList.Count;
        var processedSeries = 0;

        if (!synchronously)
        {
            await Parallel.ForEachAsync(seriesList, async (seriesBaseItem, cancellationToken) =>
            {
                // Check if the series is a valid series
                var series = seriesBaseItem as Series;
                if (series == null)
                {
                    _logger.LogWarning("Series is null!");
                    return;
                }

                await ProcessSeriesExternalSubtitles(series, cancellationToken).ConfigureAwait(false);
                Interlocked.Increment(ref processedSeries);
            }).ConfigureAwait(false);
        }
        else
        {
            foreach (var seriesBaseItem in seriesList)
            {
                // Check if the series is a valid series
                var series = seriesBaseItem as Series;
                if (series == null)
                {
                    _logger.LogWarning("Series is null!");
                    continue;
                }

                await ProcessSeriesExternalSubtitles(series, CancellationToken.None).ConfigureAwait(false);
                Interlocked.Increment(ref processedSeries);
            }
        }

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
        var subtitleLanguages = ExtractSubtitleLanguagesExternal(movie);
        if (subtitleLanguages.Count > 0)
        {
            subtitleLanguages = await Task.Run(() => AddSubtitleLanguageTags(video, subtitleLanguages), cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Added external subtitle tags for VIDEO {VideoName}: {SubtitleLanguages}", video.Name, string.Join(", ", subtitleLanguages));
        }
        else
        {
            _logger.LogWarning("No external subtitle information found for VIDEO {VideoName}", video.Name);
        }
    }

    private async Task ProcessCollectionExternalSubtitles(BoxSet collection, CancellationToken cancellationToken)
    {
        {
            if (collection is BoxSet boxSet)
            {
                var collectionItems = _libraryManager.QueryItems(new InternalItemsQuery
                {
                    IncludeItemTypes = [BaseItemKind.Movie],
                    Recursive = true,
                    ParentId = boxSet.Id
                }).Items.Select(m => m as Movie).ToList();

                if (collectionItems.Count == 0)
                {
                    _logger.LogWarning("No movies found in box set {BoxSetName}", boxSet.Name);
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

                    var movieSubtitleLanguages = ExtractSubtitleLanguagesExternal(movie);
                    collectionSubtitleLanguages.AddRange(movieSubtitleLanguages);
                }

                // Add subtitle language tags to the box set
                collectionSubtitleLanguages = collectionSubtitleLanguages.Distinct().ToList();
                if (collectionSubtitleLanguages.Count > 0)
                {
                    collectionSubtitleLanguages = await Task.Run(() => AddSubtitleLanguageTags(boxSet, collectionSubtitleLanguages), cancellationToken).ConfigureAwait(false);
                    _logger.LogInformation("Added external subtitle tags for {BoxSetName}: {Languages}", boxSet.Name, string.Join(", ", collectionSubtitleLanguages));
                }
                else
                {
                    _logger.LogWarning("No external subtitle information found for {BoxSetName}", boxSet.Name);
                }
            }
        }
    }

    private async Task ProcessSeriesExternalSubtitles(Series series, CancellationToken cancellationToken)
    {
        // Get all seasons in the series
        var seasons = GetSeasonsFromSeries(series);
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
            var episodes = GetEpisodesFromSeason(season);

            if (episodes == null || episodes.Count == 0)
            {
                _logger.LogWarning("No episodes found in SEASON {SeasonName}", season.Name);
                continue;
            }

            // Get language tags from all episodes in the season
            _logger.LogInformation("Processing SEASON {SeasonName} of {SeriesName}", season.Name, series.Name);
            var seasonSubtitleLanguages = new List<string>();
            foreach (var episode in episodes)
            {
                if (episode is Video video)
                {
                    var subtitleLanguages = ExtractSubtitleLanguagesExternal(video);
                    seasonSubtitleLanguages.AddRange(subtitleLanguages);

                    if (subtitleLanguages.Count > 0)
                    {
                        await Task.Run(() => AddSubtitleLanguageTags(season, seasonSubtitleLanguages), cancellationToken).ConfigureAwait(false);
                        _logger.LogInformation("Added external subtitle tags for VIDEO {VideoName}: {SubtitleLanguages}", video.Name, string.Join(", ", subtitleLanguages));
                    }
                    else
                    {
                        _logger.LogWarning("No external subtitle information found for VIDEO {VideoName}", video.Name);
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
                seasonSubtitleLanguages = await Task.Run(() => AddSubtitleLanguageTags(season, seasonSubtitleLanguages), cancellationToken).ConfigureAwait(false);
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
            seriesSubtitleLanguages = await Task.Run(() => AddSubtitleLanguageTags(series, seriesSubtitleLanguages), cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Added external subtitle tags for SERIES {SeriesName}: {Languages}", series.Name, string.Join(", ", seriesSubtitleLanguages));
        }
        else
        {
            _logger.LogWarning("No external subtitle information found for SERIES {SeriesName}", series.Name);
        }
    }

    // ******************************************************************
    // Helper methods
    // ******************************************************************

    private async Task<List<string>> AddAudioLanguageTagsOrUndefined(BaseItem item, List<string> audioLanguages, CancellationToken cancellationToken)
    {
        if (audioLanguages.Count > 0)
        {
            return await Task.Run(() => AddAudioLanguageTags(item, audioLanguages), cancellationToken).ConfigureAwait(false);
        }

        var disableUndTags = Plugin.Instance?.Configuration?.DisableUndefinedLanguageTags ?? false;
        if (!disableUndTags)
        {
            await Task.Run(() => AddAudioLanguageTags(item, new List<string> { "und" }), cancellationToken).ConfigureAwait(false);
            var prefix = GetAudioLanguageTagPrefix();
            _logger.LogWarning("No audio language information found for {ItemName}, added {Prefix}Undetermined", item.Name, prefix);
        }
        else
        {
            _logger.LogWarning("No audio language information found for {ItemName}, skipped adding undefined tags", item.Name);
        }

        return audioLanguages;
    }

    private string GetAudioLanguageTagPrefix()
    {
        var prefix = Plugin.Instance?.Configuration?.AudioLanguageTagPrefix;
        var subtitlePrefix = Plugin.Instance?.Configuration?.SubtitleLanguageTagPrefix;

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

    private string GetSubtitleLanguageTagPrefix()
    {
        var prefix = Plugin.Instance?.Configuration?.SubtitleLanguageTagPrefix;
        var audioPrefix = Plugin.Instance?.Configuration?.AudioLanguageTagPrefix;

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

    private List<string> StripAudioTagPrefix(IEnumerable<string> tags)
    {
        var prefix = GetAudioLanguageTagPrefix();
        return tags
            .Where(tag => tag.Length > prefix.Length)
            .Select(tag => tag.Substring(prefix.Length))
            .ToList();
    }

    private List<string> StripSubtitleTagPrefix(IEnumerable<string> tags)
    {
        var prefix = GetSubtitleLanguageTagPrefix();
        return tags
            .Where(tag => tag.Length > prefix.Length)
            .Select(tag => tag.Substring(prefix.Length))
            .ToList();
    }

    private List<string> ConvertIsoToLanguageNames(List<string> isoCodes)
    {
        var languageNames = new List<string>();

        foreach (var isoCode in isoCodes)
        {
            if (LanguageData.TryGetLanguageInfo(isoCode, out var languageInfo) && languageInfo != null && !string.IsNullOrWhiteSpace(languageInfo.Name))
            {
                languageNames.Add(languageInfo.Name);
            }
            else
            {
                // Fallback to ISO code if name not found
                _logger.LogWarning("Could not find language name for ISO code '{IsoCode}', using code as fallback", isoCode);
                languageNames.Add(isoCode);
            }
        }

        return languageNames;
    }

    private List<Movie> GetMoviesFromLibrary()
    {
        return _libraryManager.QueryItems(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Movie], // BaseItemKind.Series
            IsVirtualItem = false,
        }).Items.OfType<Movie>().ToList();
    }

    private List<Series> GetSeriesFromLibrary()
    {
        return _libraryManager.QueryItems(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Series],
            Recursive = true,
        }).Items.OfType<Series>().ToList();
    }

    private List<BoxSet> GetBoxSetsFromLibrary()
    {
        return _libraryManager.QueryItems(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.BoxSet],
            CollapseBoxSetItems = false,
            Recursive = true,
            HasTmdbId = true
        }).Items.OfType<BoxSet>().ToList();
    }

    private List<Season> GetSeasonsFromSeries(Series series)
    {
        return _libraryManager.QueryItems(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Season],
            Recursive = true,
            ParentId = series.Id,
            IsVirtualItem = false
        }).Items.OfType<Season>().ToList();
    }

    private List<Episode> GetEpisodesFromSeason(Season season)
    {
        return _libraryManager.QueryItems(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Episode],
            Recursive = true,
            ParentId = season.Id,
            IsVirtualItem = false
        }).Items.OfType<Episode>().ToList();
    }

    private async Task<(List<string> AudioLanguages, List<string> SubtitleLanguages)> ProcessVideo(Video video, bool subtitleTags, CancellationToken cancellationToken)
    {
        var audioLanguages = new List<string>();
        var subtitleLanguages = new List<string>();

        try
        {
            // Get media sources from the video
            var mediaSources = video.GetMediaSources(false);

            foreach (var source in mediaSources)
            {
                // Extract audio languages from audio streams
                var audioStreams = source.MediaStreams?
                    .Where(s => s.Type == MediaBrowser.Model.Entities.MediaStreamType.Audio);

                if (audioStreams != null)
                {
                    foreach (var stream in audioStreams)
                    {
                        var langCode = stream.Language;
                        if (!string.IsNullOrEmpty(langCode) &&
                            !langCode.Equals("und", StringComparison.OrdinalIgnoreCase) &&
                            !langCode.Equals("root", StringComparison.OrdinalIgnoreCase))
                        {
                            // Convert 2-letter codes to 3-letter codes
                            var threeLetterCode = ConvertToThreeLetterIsoCode(langCode);
                            audioLanguages.Add(threeLetterCode);
                        }
                    }
                }

                // Extract subtitle languages if enabled
                if (subtitleTags)
                {
                    var subtitleStreams = source.MediaStreams?
                        .Where(s => s.Type == MediaBrowser.Model.Entities.MediaStreamType.Subtitle);

                    if (subtitleStreams != null)
                    {
                        foreach (var stream in subtitleStreams)
                        {
                            var langCode = stream.Language;
                            if (!string.IsNullOrEmpty(langCode) &&
                                !langCode.Equals("und", StringComparison.OrdinalIgnoreCase) &&
                                !langCode.Equals("root", StringComparison.OrdinalIgnoreCase))
                            {
                                // Convert 2-letter codes to 3-letter codes
                                var threeLetterCode = ConvertToThreeLetterIsoCode(langCode);
                                subtitleLanguages.Add(threeLetterCode);
                            }
                        }
                    }
                }
            }

            // Get external subtitle files as well
            if (subtitleTags)
            {
                var externalSubtitles = ExtractSubtitleLanguagesExternal(video);
                subtitleLanguages.AddRange(externalSubtitles);
            }

            // Remove duplicates
            audioLanguages = audioLanguages.Distinct().ToList();
            subtitleLanguages = subtitleLanguages.Distinct().ToList();

            if (audioLanguages.Count > 0)
            {
                // Add audio language tags
                audioLanguages = await Task.Run(() => AddAudioLanguageTags(video, audioLanguages), cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Added audio tags for VIDEO {VideoName}: {AudioLanguages}", video.Name, string.Join(", ", audioLanguages));
            }
            else
            {
                audioLanguages = await AddAudioLanguageTagsOrUndefined(video, audioLanguages, cancellationToken).ConfigureAwait(false);
            }

            if (subtitleTags && subtitleLanguages.Count > 0)
            {
                // Add subtitle language tags
                subtitleLanguages = await Task.Run(() => AddSubtitleLanguageTags(video, subtitleLanguages), cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Added subtitle tags for VIDEO {VideoName}: {SubtitleLanguages}", video.Name, string.Join(", ", subtitleLanguages));
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

    private bool HasAudioLanguageTags(BaseItem item)
    {
        var prefix = GetAudioLanguageTagPrefix();
        return item.Tags.Any(tag => tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private bool HasSubtitleLanguageTags(BaseItem item)
    {
        var prefix = GetSubtitleLanguageTagPrefix();
        return item.Tags.Any(tag => tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private List<string> GetAudioLanguageTags(BaseItem item)
    {
        var prefix = GetAudioLanguageTagPrefix();
        return item.Tags.Where(tag => tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private List<string> GetSubtitleLanguageTags(BaseItem item)
    {
        var prefix = GetSubtitleLanguageTagPrefix();
        return item.Tags.Where(tag => tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void RemoveAudioLanguageTags(BaseItem item)
    {
        var prefix = GetAudioLanguageTagPrefix();
        foreach (var tag in item.Tags.Where(tag => tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            var name = tag.ToString();
            var current = item.Tags;
            if (current.Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                item.Tags = current.Where(tag => !tag.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
            }
        }
    }

    private void RemoveSubtitleLanguageTags(BaseItem item)
    {
        var prefix = GetSubtitleLanguageTagPrefix();
        foreach (var tag in item.Tags.Where(tag => tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            var name = tag.ToString();
            var current = item.Tags;
            if (current.Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                item.Tags = current.Where(tag => !tag.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
            }
        }
    }

    private List<string> ExtractSubtitleLanguagesExternal(Video video)
    {
        var subtitleLanguagesExternal = new List<string>();

        if (video.HasSubtitles)
        {
            // Get the subtitle files
            var subtitleFiles = video.SubtitleFiles.ToList();
            foreach (var subtitleFile in subtitleFiles)
            {
                // Extract the language code from the file name
                var subtitleRegexExternal = new Regex(@"\.(\w{2,3})\.");
                foreach (Match match in subtitleRegexExternal.Matches(subtitleFile))
                {
                    var languageCode = match.Groups[1].Value.ToLowerInvariant();
                    if (LanguageData.IsValidLanguageCode(languageCode))
                    {
                        // Convert 2-letter ISO codes to 3-letter ISO codes
                        var threeLetterCode = ConvertToThreeLetterIsoCode(languageCode);
                        subtitleLanguagesExternal.Add(threeLetterCode); // e.g., "eng", "ger"
                    }
                }
            }

            // Remove duplicates
            subtitleLanguagesExternal = subtitleLanguagesExternal.Distinct().ToList();

            _logger.LogInformation("Final external subtitle languages for {VideoName}: [{Languages}]", video.Name, string.Join(", ", subtitleLanguagesExternal));
        }

        return subtitleLanguagesExternal;
    }

    private List<string> FilterOutLanguages(BaseItem item, List<string> languages)
    {
        // Get the whitelist of language tags
        var whitelist = Plugin.Instance?.Configuration?.WhitelistLanguageTags ?? string.Empty;
        var whitelistArray = whitelist.Split(Separator, StringSplitOptions.RemoveEmptyEntries).Select(lang => lang.Trim()).ToList();
        var filteredOutLanguages = new List<string>();

        // Check if whitelistArray has entries
        if (whitelistArray.Count > 0)
        {
            // Add und(efined) to the whitelist
            whitelistArray.Add("und");
            // Remmove duplicates
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

    private List<string> AddAudioLanguageTags(BaseItem item, List<string> languages)
    {
        // Filter out languages based on the whitelist (ISO codes)
        languages = FilterOutLanguages(item, languages);

        // Convert ISO codes to language names
        var languageNames = ConvertIsoToLanguageNames(languages);

        // Remove duplicates (in case multiple ISO codes map to same name)
        languageNames = languageNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var prefix = GetAudioLanguageTagPrefix();
        foreach (var languageName in languageNames)
        {
            string tag = $"{prefix}{languageName}";

            // Avoid duplicates
            if (!item.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            {
                item.AddTag(tag);
            }
        }

        // Save the changes
        var parent = item.GetParent();
        _libraryManager.UpdateItemAsync(item: item, parent: parent, updateReason: ItemUpdateType.MetadataEdit, cancellationToken: CancellationToken.None).GetAwaiter().GetResult();

        return languages;
    }

    private List<string> AddAudioLanguageTagsByName(BaseItem item, List<string> languages)
    {
        // Remove duplicates
        var languageNames = languages.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var prefix = GetAudioLanguageTagPrefix();
        foreach (var languageName in languageNames)
        {
            string tag = $"{prefix}{languageName}";

            // Avoid duplicates
            if (!item.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            {
                item.AddTag(tag);
            }
        }

        // Save the changes
        var parent = item.GetParent();
        _libraryManager.UpdateItemAsync(item: item, parent: parent, updateReason: ItemUpdateType.MetadataEdit, cancellationToken: CancellationToken.None).GetAwaiter().GetResult();

        return languages;
    }

    private List<string> AddSubtitleLanguageTags(BaseItem item, List<string> languages)
    {
        // Filter out languages based on the whitelist (ISO codes)
        languages = FilterOutLanguages(item, languages);

        // Convert ISO codes to language names
        var languageNames = ConvertIsoToLanguageNames(languages);

        // Remove duplicates (in case multiple ISO codes map to same name)
        languageNames = languageNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var prefix = GetSubtitleLanguageTagPrefix();
        foreach (var languageName in languageNames)
        {
            string tag = $"{prefix}{languageName}";

            // Avoid duplicates
            if (!item.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            {
                item.AddTag(tag);
            }
        }

        // Save the changes
        var parent = item.GetParent();
        _libraryManager.UpdateItemAsync(item: item, parent: parent, updateReason: ItemUpdateType.MetadataEdit, cancellationToken: CancellationToken.None).GetAwaiter().GetResult();

        return languages;
    }

    private List<string> AddSubtitleLanguageTagsByName(BaseItem item, List<string> languages)
    {
        // Remove duplicates
        var languageNames = languages.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var prefix = GetSubtitleLanguageTagPrefix();
        foreach (var languageName in languageNames)
        {
            string tag = $"{prefix}{languageName}";

            // Avoid duplicates
            if (!item.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            {
                item.AddTag(tag);
            }
        }

        // Save the changes
        var parent = item.GetParent();
        _libraryManager.UpdateItemAsync(item: item, parent: parent, updateReason: ItemUpdateType.MetadataEdit, cancellationToken: CancellationToken.None).GetAwaiter().GetResult();

        return languages;
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
    /// Converts a language code to its 3-letter ISO 639-2 equivalent.
    /// If the input is already a 3-letter code, returns it as-is.
    /// If the input is a 2-letter ISO 639-1 code, converts it to the corresponding 3-letter code.
    /// </summary>
    /// <param name="languageCode">The language code to convert.</param>
    /// <returns>The 3-letter ISO 639-2 language code.</returns>
    private string ConvertToThreeLetterIsoCode(string languageCode)
    {
        // If it's already a 3-letter code, return as-is
        if (languageCode.Length == 3)
        {
            return languageCode;
        }

        // If it's a 2-letter code, try to get the corresponding 3-letter code
        if (languageCode.Length == 2 && LanguageData.TryGetLanguageInfo(languageCode, out var languageInfo) && languageInfo != null)
        {
            // Prefer Iso6392 (3-letter code), but fall back to Iso6392B if needed
            return !string.IsNullOrEmpty(languageInfo.Iso6392) ? languageInfo.Iso6392 :
                   !string.IsNullOrEmpty(languageInfo.Iso6392B) ? languageInfo.Iso6392B : languageCode;
        }

        // If we can't convert it, return the original code
        return languageCode;
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
