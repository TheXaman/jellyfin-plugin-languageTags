using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
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
using MediaBrowser.Controller.MediaEncoding;
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
    private readonly IMediaEncoder _mediaEncoder;
    private static readonly char[] Separator = new[] { ',' };

    /// <summary>
    /// Initializes a new instance of the <see cref="LanguageTagsManager"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="collectionManager">Instance of the <see cref="ICollectionManager"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{LanguageTagsManager}"/> interface.</param>
    /// <param name="mediaEncoder">Instance of the <see cref="IMediaEncoder"/> interface.</param>
    public LanguageTagsManager(ILibraryManager libraryManager, ICollectionManager collectionManager, ILogger<LanguageTagsManager> logger, IMediaEncoder mediaEncoder)
    {
        _libraryManager = libraryManager;
        _collectionManager = collectionManager;
        _logger = logger;
        _mediaEncoder = mediaEncoder;
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
        // Check if the ffmpeg / encoder path is set
        if (string.IsNullOrEmpty(_mediaEncoder.EncoderPath))
        {
            _logger.LogError("FFmpeg / encoder path is not set");
            return;
        }

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

        // Print the ffmpeg / encoder path
        GetFFmpegPath(true);

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
                await ProcessLibraryExternalSubtitles(synchronously).ConfigureAwait(false);
                break;
            default:
                // Process movies
                await ProcessLibraryMovies(fullScan, synchronously, subtitleTags).ConfigureAwait(false);

                // Process series
                await ProcessLibrarySeries(fullScan, synchronously, subtitleTags).ConfigureAwait(false);

                // Process box sets / collections
                await ProcessLibraryCollections(fullScan, synchronously, subtitleTags).ConfigureAwait(false);
                break;
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
            if (!HasValidPath(video.Path))
            {
                _logger.LogWarning("Invalid file path for {VideoName}", video.Name);
                return;
            }

            if (HasAudioLanguageTags(video))
            {
                if (!fullScan)
                {
                    _logger.LogInformation("Aduio tags exist, skipping {VideoName}", video.Name);
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
                return;
            }

            // Get language tags from all episodes in the season
            _logger.LogInformation("Processing SEASON {SeasonName} of {SeriesName}", season.Name, series.Name);
            var seasonAudioLanguages = new List<string>();
            var seasonSubtitleLanguages = new List<string>();
            foreach (var episode in episodes)
            {
                if (episode is Video video)
                {
                    if (!HasValidPath(video.Path))
                    {
                        _logger.LogWarning("Invalid file path for {VideoName}", video.Name);
                        return;
                    }

                    // Check if the video has subtitle language tags and subtitleTags is enabled
                    if (HasSubtitleLanguageTags(video) && subtitleTags)
                    {
                        if (!fullScan)
                        {
                            _logger.LogInformation("Subtitle tags exist for {VideoName}", video.Name);
                            var episodeLanguagesTmp = GetSubtitleLanguageTags(video);
                            seasonSubtitleLanguages.AddRange(episodeLanguagesTmp);
                        }

                        RemoveSubtitleLanguageTags(episode);
                    }

                    // Check if the video has audio language tags
                    if (HasAudioLanguageTags(video))
                    {
                        if (!fullScan)
                        {
                            _logger.LogInformation("Audio tags exist, skipping {VideoName}", video.Name);
                            var episodeLanguagesTmp = GetAudioLanguageTags(video);
                            seasonAudioLanguages.AddRange(episodeLanguagesTmp);
                            return;
                        }

                        RemoveAudioLanguageTags(episode);
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
                await Task.Run(() => AddAudioLanguageTags(season, new List<string> { "und" }), cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("No audio language information found for SEASON {SeasonName} of {SeriesName}, added language_und(efined)", season.Name, series.Name);
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
            await Task.Run(() => AddAudioLanguageTags(series, new List<string> { "und" }), cancellationToken).ConfigureAwait(false);
            _logger.LogWarning("No audio language information found for SERIES {SeriesName}, added language_und(efined)", series.Name);
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
            var collectionItems = boxSet.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.Movie],
                Recursive = true
            }).Select(m => m as Movie).ToList();

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
                await Task.Run(() => AddAudioLanguageTags(boxSet, new List<string> { "und" }), cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("No audio language information found for {BoxSetName}, added language_und(efined)", boxSet.Name);
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
    private async Task ProcessLibraryExternalSubtitles(bool synchronously)
    {
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

        if (!HasValidPath(video.Path))
        {
            _logger.LogWarning("Invalid file path for {VideoName}", video.Name);
            return;
        }

        // Check if the video has subtitle language tags
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
                var collectionItems = boxSet.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = [BaseItemKind.Movie],
                    Recursive = true
                }).Select(m => m as Movie).ToList();

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

                // Strip "subtitle_language_" prefix
                collectionSubtitleLanguages = collectionSubtitleLanguages.Select(lang => lang.Substring(18)).ToList();

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
                return;
            }

            // Get language tags from all episodes in the season
            _logger.LogInformation("Processing SEASON {SeasonName} of {SeriesName}", season.Name, series.Name);
            var seasonSubtitleLanguages = new List<string>();
            foreach (var episode in episodes)
            {
                if (episode is Video video)
                {
                    if (!HasValidPath(video.Path))
                    {
                        _logger.LogWarning("Invalid file path for {VideoName}", video.Name);
                        return;
                    }

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
        return [.. _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Movie], // BaseItemKind.Series
            IsVirtualItem = false,
        }).Select(m => m as Movie)];
    }

    private List<Series> GetSeriesFromLibrary()
    {
        return [.. _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Series],
            Recursive = true,
        }).Select(s => s as Series)];
    }

    private List<BoxSet> GetBoxSetsFromLibrary()
    {
        return [.. _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.BoxSet],
            CollapseBoxSetItems = false,
            Recursive = true,
            HasTmdbId = true
        }).Select(b => b as BoxSet)];
    }

    private List<Season> GetSeasonsFromSeries(Series series)
    {
        return [.. series.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Season],
            Recursive = true
        }).Select(s => s as Season)];
    }

    private List<Episode> GetEpisodesFromSeason(Season season)
    {
        return [.. season.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Episode],
            Recursive = true
        }).Select(e => e as Episode)];
    }

    private async Task<(List<string> AudioLanguages, List<string> SubtitleLanguages)> ProcessVideo(Video video, bool subtitleTags, CancellationToken cancellationToken)
    {
        var audioLanguages = new List<string>();
        var subtitleLanguages = new List<string>();

        try
        {
            // Step 1: Run FFmpeg
            string ffmpegOutput = await Task.Run(() => RunFFmpeg(video.Path)).ConfigureAwait(false);

            // Step 2: Extract audio languages
            audioLanguages = await Task.Run(() => ExtractAudioLanguages(ffmpegOutput)).ConfigureAwait(false);

            if (audioLanguages.Count > 0)
            {
                // Step 3: Add audio language tags
                audioLanguages = await Task.Run(() => AddAudioLanguageTags(video, audioLanguages), cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Added audio tags for VIDEO {VideoName}: {AudioLanguages}", video.Name, string.Join(", ", audioLanguages));
            }
            else
            {
                await Task.Run(() => AddAudioLanguageTags(video, new List<string> { "und" }), cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("No audio language information found for VIDEO {VideoName}, added language_und(efined)", video.Name);
            }

            if (!subtitleTags) // skip subtitle tags
            {
                return (audioLanguages, subtitleLanguages);
            }

            // Step 4: Extract subtitle languages
            subtitleLanguages = await Task.Run(() => ExtractSubtitleLanguages(ffmpegOutput, video)).ConfigureAwait(false);

            if (subtitleLanguages.Count > 0)
            {
                // Step 5: Add subtitle language tags
                subtitleLanguages = await Task.Run(() => AddSubtitleLanguageTags(video, subtitleLanguages), cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Added subtitle tags for VIDEO {VideoName}: {SubtitleLanguages}", video.Name, string.Join(", ", subtitleLanguages));
            }
            else
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

    private bool HasValidPath(string filePath)
    {
        return !string.IsNullOrEmpty(filePath) && File.Exists(filePath);
    }

    private string GetFFmpegPath(bool printPath = false)
    {
        var encoderPath = _mediaEncoder.EncoderPath;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (printPath)
            {
                _logger.LogInformation("Windows detected");
                _logger.LogInformation("Encoder path: {EncoderPath}", encoderPath);
            }

            return encoderPath;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (printPath)
            {
                _logger.LogInformation("Linux detected");
                _logger.LogInformation("Encoder path: {EncoderPath}", encoderPath);
            }

            return encoderPath;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (printPath)
            {
                _logger.LogInformation("macOS detected");
                _logger.LogInformation("Encoder path: {EncoderPath}", encoderPath);
            }

            return encoderPath;
        }
        else
        {
            throw new NotSupportedException("Unsupported operating system");
        }
    }

    private async Task<string> RunFFmpeg(string filePath)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = GetFFmpegPath(),
                Arguments = $"-i \"{filePath}\" -hide_banner",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        var outputBuilder = new StringBuilder();
        process.ErrorDataReceived += (sender, args) => outputBuilder.AppendLine(args.Data);

        process.Start();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync().ConfigureAwait(false);

        return outputBuilder.ToString();
    }

    private List<string> ExtractAudioLanguages(string ffmpegOutput)
    {
        var audioLanguages = new List<string>();
        var audioRegex = new Regex(@"\(\s*(\w{3})\s*\):\s*Audio");

        foreach (Match match in audioRegex.Matches(ffmpegOutput))
        {
            audioLanguages.Add(match.Groups[1].Value); // e.g., "eng", "ger"
        }

        // Filter out tags that are not ISO 639-2/B language codes
        audioLanguages = audioLanguages.Where(lang => lang.Length == 3).ToList();

        // Remove duplicates
        audioLanguages = audioLanguages.Distinct().ToList();

        return audioLanguages;
    }

    private List<string> ExtractSubtitleLanguages(string ffmpegOutput, Video video)
    {
        var subtitleLanguages = new List<string>();

        // Extract subtitle languages from ffmpeg output
        var subtitleRegex = new Regex(@"\(\s*(\w{3})\s*\):\s*Subtitle");

        foreach (Match match in subtitleRegex.Matches(ffmpegOutput))
        {
            subtitleLanguages.Add(match.Groups[1].Value); // e.g., "eng", "ger"
        }

        // Get the subtitle languages from external files
        var subtitleLanguagesExternal = ExtractSubtitleLanguagesExternal(video);

        // Combine and filter out duplicates
        subtitleLanguages.AddRange(subtitleLanguagesExternal);
        subtitleLanguages = subtitleLanguages.Distinct().ToList();

        // Filter out tags based on the whitelist and ISO 639-2/B language codes
        if (subtitleLanguages.Count > 0)
        {
            subtitleLanguages = FilterOutLanguages(video, subtitleLanguages);
        }

        return subtitleLanguages;
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
                var match = Regex.Match(subtitleFile, @"\.(\w{3})\.");
                if (match.Success)
                {
                    subtitleLanguagesExternal.Add(match.Groups[1].Value); // e.g., "eng", "ger"
                }
            }

            // Filter out tags that are not ISO 639-2/B language codes
            subtitleLanguagesExternal = subtitleLanguagesExternal.Where(lang => lang.Length == 3).ToList();

            // Remove duplicates
            subtitleLanguagesExternal = subtitleLanguagesExternal.Distinct().ToList();
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
