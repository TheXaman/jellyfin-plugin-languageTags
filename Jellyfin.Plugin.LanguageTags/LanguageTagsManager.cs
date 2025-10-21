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
    private readonly HashSet<string> _queuedTmdbCollectionIds;
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
        _queuedTmdbCollectionIds = new HashSet<string>();
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
                    return;
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
                            var episodeLanguagesTmp = GetSubtitleLanguageTags(video).Select(lang => lang.Substring(18)).ToList(); // Strip "subtitle_language_" prefix
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
                            var episodeLanguagesTmp = GetAudioLanguageTags(video).Select(lang => lang.Substring(9)).ToList(); // Strip "language_" prefix
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
            if (seasonAudioLanguages.Count > 0)
            {
                seasonAudioLanguages = await Task.Run(() => AddAudioLanguageTags(season, seasonAudioLanguages), cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Added audio tags for SEASON {SeasonName} of {SeriesName}: {Languages}", season.Name, series.Name, string.Join(", ", seasonAudioLanguages));
            }
            else
            {
                var disableUndTags = Plugin.Instance?.Configuration?.DisableUndefinedLanguageTags ?? false;
                if (!disableUndTags)
                {
                    await Task.Run(() => AddAudioLanguageTags(season, new List<string> { "und" }), cancellationToken).ConfigureAwait(false);
                    _logger.LogWarning("No audio language information found for SEASON {SeasonName} of {SeriesName}, added language_und(efined)", season.Name, series.Name);
                }
                else
                {
                    _logger.LogWarning("No audio language information found for SEASON {SeasonName} of {SeriesName}, skipped adding undefined tags", season.Name, series.Name);
                }
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
        if (seriesAudioLanguages.Count > 0)
        {
            seriesAudioLanguages = await Task.Run(() => AddAudioLanguageTags(series, seriesAudioLanguages), cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Added audio tags for SERIES {SeriesName}: {Languages}", series.Name, string.Join(", ", seriesAudioLanguages));
        }
        else
        {
            var disableUndTags = Plugin.Instance?.Configuration?.DisableUndefinedLanguageTags ?? false;
            if (!disableUndTags)
            {
                await Task.Run(() => AddAudioLanguageTags(series, new List<string> { "und" }), cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("No audio language information found for SERIES {SeriesName}, added language_und(efined)", series.Name);
            }
            else
            {
                _logger.LogWarning("No audio language information found for SERIES {SeriesName}, skipped adding undefined tags", series.Name);
            }
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
                    return;
                }

                var movieLanguages = GetAudioLanguageTags(movie);
                collectionAudioLanguages.AddRange(movieLanguages);

                var movieSubtitleLanguages = GetSubtitleLanguageTags(movie);
                collectionSubtitleLanguages.AddRange(movieSubtitleLanguages);
            }

            // Strip "language_" prefix
            collectionAudioLanguages = collectionAudioLanguages.Select(lang => lang.Substring(9)).ToList();

            // Add language tags to the box set
            collectionAudioLanguages = collectionAudioLanguages.Distinct().ToList();
            if (collectionAudioLanguages.Count > 0)
            {
                collectionAudioLanguages = await Task.Run(() => AddAudioLanguageTags(boxSet, collectionAudioLanguages), cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Added audio tags for {BoxSetName}: {Languages}", boxSet.Name, string.Join(", ", collectionAudioLanguages));
            }
            else
            {
                var disableUndTags = Plugin.Instance?.Configuration?.DisableUndefinedLanguageTags ?? false;
                if (!disableUndTags)
                {
                    await Task.Run(() => AddAudioLanguageTags(boxSet, new List<string> { "und" }), cancellationToken).ConfigureAwait(false);
                    _logger.LogWarning("No audio language information found for {BoxSetName}, added language_und(efined)", boxSet.Name);
                }
                else
                {
                    _logger.LogWarning("No audio language information found for {BoxSetName}, skipped adding undefined tags", boxSet.Name);
                }
            }

            if (!subtitleTags) // skip subtitle tags
            {
                return;
            }

            // Strip "subtitle_language_" prefix
            collectionSubtitleLanguages = collectionSubtitleLanguages.Select(lang => lang.Substring(18)).ToList();

            // Add subtitle language tags to the box set
            collectionSubtitleLanguages = collectionSubtitleLanguages.Distinct().ToList();
            if (collectionSubtitleLanguages.Count > 0)
            {
                collectionSubtitleLanguages = await Task.Run(() => AddSubtitleLanguageTags(boxSet, collectionSubtitleLanguages), cancellationToken).ConfigureAwait(false);
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
                    return;
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
                        return;
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
                var disableUndTags = Plugin.Instance?.Configuration?.DisableUndefinedLanguageTags ?? false;
                if (!disableUndTags)
                {
                    await Task.Run(() => AddAudioLanguageTags(video, new List<string> { "und" }), cancellationToken).ConfigureAwait(false);
                    _logger.LogWarning("No audio language information found for VIDEO {VideoName}, added language_und(efined)", video.Name);
                }
                else
                {
                    _logger.LogWarning("No audio language information found for VIDEO {VideoName}, skipped adding undefined tags", video.Name);
                }
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
        return item.Tags.Any(tag => tag.StartsWith("language_", StringComparison.OrdinalIgnoreCase));
    }

    private bool HasSubtitleLanguageTags(BaseItem item)
    {
        return item.Tags.Any(tag => tag.StartsWith("subtitle_language_", StringComparison.OrdinalIgnoreCase));
    }

    private List<string> GetAudioLanguageTags(BaseItem item)
    {
        return item.Tags.Where(tag => tag.StartsWith("language_", StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private List<string> GetSubtitleLanguageTags(BaseItem item)
    {
        return item.Tags.Where(tag => tag.StartsWith("subtitle_language_", StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void RemoveAudioLanguageTags(BaseItem item)
    {
        foreach (var tag in item.Tags.Where(tag => tag.StartsWith("language_", StringComparison.OrdinalIgnoreCase)).ToList())
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
        foreach (var tag in item.Tags.Where(tag => tag.StartsWith("subtitle_language_", StringComparison.OrdinalIgnoreCase)).ToList())
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
        // Filter out languages based on the whitelist
        languages = FilterOutLanguages(item, languages);

        foreach (var language in languages)
        {
            string tag = $"language_{language}";

            // Avoid duplicates
            if (!item.Tags.Contains(tag))
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
        // Filter out languages based on the whitelist
        languages = FilterOutLanguages(item, languages);

        foreach (var language in languages)
        {
            string tag = $"subtitle_language_{language}";

            // Avoid duplicates
            if (!item.Tags.Contains(tag))
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
