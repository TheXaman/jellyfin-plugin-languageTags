using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

        switch (type.ToLowerInvariant())
        {
            case "movies":
                await ProcessLibraryMovies(fullScan, synchronously).ConfigureAwait(false);
                break;
            case "series":
                await ProcessLibrarySeries(fullScan, synchronously).ConfigureAwait(false);
                break;
            case "collections":
                await ProcessLibraryCollections(fullScan, synchronously).ConfigureAwait(false);
                break;
            default:
                // Process movies
                await ProcessLibraryMovies(fullScan, synchronously).ConfigureAwait(false);

                // Process series
                await ProcessLibrarySeries(fullScan, synchronously).ConfigureAwait(false);

                // Process box sets / collections
                await ProcessLibraryCollections(fullScan, synchronously).ConfigureAwait(false);
                break;
        }
    }

    /// <summary>
    /// Processes the libraries movies.
    /// </summary>
    /// <param name="fullScan">if set to <c>true</c> [full scan].</param>
    /// <param name="synchronously">if set to <c>true</c> [synchronously].</param>
    /// <returns>A <see cref="Task"/> representing the library scan progress.</returns>
    private async Task ProcessLibraryMovies(bool fullScan, bool synchronously)
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
                await ProcessMovie(item, fullScan, cancellationToken).ConfigureAwait(false);

                Interlocked.Increment(ref processedMovies);
            }).ConfigureAwait(false);
        }
        else
        {
            foreach (var movie in movies)
            {
                await ProcessMovie(movie, fullScan, CancellationToken.None).ConfigureAwait(false);

                Interlocked.Increment(ref processedMovies);
            }
        }

        _logger.LogInformation("Processed {ProcessedMovies} of {TotalMovies} movies", processedMovies, totalMovies);
    }

    private async Task ProcessMovie(Movie movie, bool fullScan, CancellationToken cancellationToken)
    {
        if (movie is Video video)
        {
            if (!HasValidPath(video.Path))
            {
                _logger.LogWarning("Invalid file path for {VideoName}", video.Name);
                return;
            }

            if (HasLanguageTags(video))
            {
                if (!fullScan)
                {
                    _logger.LogInformation("Tags exist, skipping {VideoName}", video.Name);
                    return;
                }

                RemoveLanguageTags(video);
            }

            await ProcessVideo(video, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Processes the libraries series.
    /// </summary>
    /// <param name="fullScan">if set to <c>true</c> [full scan].</param>
    /// <param name="synchronously">if set to <c>true</c> [synchronously].</param>
    /// <returns>A <see cref="Task"/> representing the library scan progress.</returns>
    private async Task ProcessLibrarySeries(bool fullScan, bool synchronously)
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

                await ProcessSeries(series, fullScan, cancellationToken).ConfigureAwait(false);
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

                await ProcessSeries(series, fullScan, CancellationToken.None).ConfigureAwait(false);
                Interlocked.Increment(ref processedSeries);
            }
        }

        _logger.LogInformation("Processed {ProcessedSeries} of {TotalSeries} series", processedSeries, totalSeries);
    }

    private async Task ProcessSeries(Series series, bool fullScan, CancellationToken cancellationToken)
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
        var seriesLanguages = new List<string>();
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
            var seasonLanguages = new List<string>();
            foreach (var episode in episodes)
            {
                if (episode is Video video)
                {
                    if (!HasValidPath(video.Path))
                    {
                        _logger.LogWarning("Invalid file path for {VideoName}", video.Name);
                        return;
                    }

                    if (HasLanguageTags(video))
                    {
                        if (!fullScan)
                        {
                            _logger.LogInformation("Tags exist, skipping {VideoName}", video.Name);
                            var episodeLanguagesTmp = GetLanguageTags(video);
                            seasonLanguages.AddRange(episodeLanguagesTmp);
                            return;
                        }

                        RemoveLanguageTags(episode);
                    }

                    var episodeLanguages = await ProcessVideo(video, cancellationToken).ConfigureAwait(false);
                    seasonLanguages.AddRange(episodeLanguages);
                }
            }

            // Make sure we have unique language tags
            seasonLanguages = seasonLanguages.Distinct().ToList();

            // Add the season languages to the series languages
            seriesLanguages.AddRange(seasonLanguages);

            // Remove existing language tags
            RemoveLanguageTags(season);

            // Add language tags to the season
            if (seasonLanguages.Count > 0)
            {
                seasonLanguages = await Task.Run(() => AddLanguageTags(season, seasonLanguages), cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Added tags for SEASON {SeasonName} of {SeriesName}: {Languages}", season.Name, series.Name, string.Join(", ", seasonLanguages));
            }
            else
            {
                await Task.Run(() => AddLanguageTags(season, new List<string> { "und" }), cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("No language information found for SEASON {SeasonName} of {SeriesName}, added language_und(efined)", season.Name, series.Name);
            }
        }

        // Remove existing language tags
        RemoveLanguageTags(series);

        // Add language tags to the series
        seriesLanguages = seriesLanguages.Distinct().ToList();
        if (seriesLanguages.Count > 0)
        {
            seriesLanguages = await Task.Run(() => AddLanguageTags(series, seriesLanguages), cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Added tags for SERIES {SeriesName}: {Languages}", series.Name, string.Join(", ", seriesLanguages));
        }
        else
        {
            await Task.Run(() => AddLanguageTags(series, new List<string> { "und" }), cancellationToken).ConfigureAwait(false);
            _logger.LogWarning("No language information found for SERIES {SeriesName}, added language_und(efined)", series.Name);
        }
    }

    /// <summary>
    /// Processes the libraries collections.
    /// </summary>
    /// <param name="fullScan">if set to <c>true</c> [full scan].</param>
    /// <param name="synchronously">if set to <c>true</c> [synchronously].</param>
    /// <returns>A <see cref="Task"/> representing the library scan progress.</returns>
    private async Task ProcessLibraryCollections(bool fullScan, bool synchronously)
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
                await ProcessCollection(item, fullScan, cancellationToken).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        else
        {
            foreach (var collection in collections)
            {
                await ProcessCollection(collection, fullScan, CancellationToken.None).ConfigureAwait(false);
            }
        }

        _logger.LogInformation("Processed {TotalCollections} collections", totalCollections);
    }

    private async Task ProcessCollection(BoxSet collection, bool fullRefresh, CancellationToken cancellationToken)
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
            RemoveLanguageTags(collection);

            // Get language tags from all movies in the box set
            var collectionLanguages = new List<string>();
            foreach (var movie in collectionItems)
            {
                if (movie == null)
                {
                    _logger.LogWarning("Movie is null!");
                    return;
                }

                var movieLanguages = GetLanguageTags(movie);
                collectionLanguages.AddRange(movieLanguages);
            }

            // Strip "language_" prefix
            collectionLanguages = collectionLanguages.Select(lang => lang.Substring(9)).ToList();

            // Add language tags to the box set
            collectionLanguages = collectionLanguages.Distinct().ToList();
            if (collectionLanguages.Count > 0)
            {
                collectionLanguages = await Task.Run(() => AddLanguageTags(boxSet, collectionLanguages), cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Added tags for {BoxSetName}: {Languages}", boxSet.Name, string.Join(", ", collectionLanguages));
            }
            else
            {
                await Task.Run(() => AddLanguageTags(boxSet, new List<string> { "und" }), cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("No language information found for {BoxSetName}, added language_und(efined)", boxSet.Name);
            }
        }
    }

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

    private async Task<List<string>> ProcessVideo(Video video, CancellationToken cancellationToken)
    {
        var videoLanguages = new List<string>();
        try
        {
            // Step 1: Run FFmpeg
            string ffmpegOutput = await Task.Run(() => RunFFmpeg(video.Path)).ConfigureAwait(false);

            // Step 2: Extract languages
            videoLanguages = await Task.Run(() => ExtractLanguages(ffmpegOutput)).ConfigureAwait(false);

            // Filter out tags that are not ISO 639-2/B language codes
            videoLanguages = videoLanguages.Where(lang => lang.Length == 3).ToList();

            if (videoLanguages.Count > 0)
            {
                // Step 2.5: Remove duplicates
                videoLanguages = videoLanguages.Distinct().ToList();

                // Step 3: Add language tags
                videoLanguages = await Task.Run(() => AddLanguageTags(video, videoLanguages), cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Added tags for VIDEO {VideoName}: {VideoLanguages}", video.Name, string.Join(", ", videoLanguages));
            }
            else
            {
                await Task.Run(() => AddLanguageTags(video, new List<string> { "und" }), cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("No language information found for VIDEO {VideoName}, added language_und(efined)", video.Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing {VideoName}", video.Name);
        }

        return videoLanguages;
    }

    private bool HasLanguageTags(BaseItem item)
    {
        return item.Tags.Any(tag => tag.StartsWith("language_", StringComparison.OrdinalIgnoreCase));
    }

    private List<string> GetLanguageTags(BaseItem item)
    {
        return item.Tags.Where(tag => tag.StartsWith("language_", StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void RemoveLanguageTags(BaseItem item)
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

    private bool HasValidPath(string filePath)
    {
        return !string.IsNullOrEmpty(filePath) && File.Exists(filePath);
    }

    private async Task<string> RunFFmpeg(string filePath)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "/usr/lib/jellyfin-ffmpeg/ffmpeg",
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

    private List<string> ExtractLanguages(string ffmpegOutput)
    {
        var languages = new List<string>();
        var regex = new Regex(@"\(\s*(\w{3})\s*\):\s*Audio");

        foreach (Match match in regex.Matches(ffmpegOutput))
        {
            languages.Add(match.Groups[1].Value); // e.g., "eng", "ger"
        }

        return languages;
    }

    private List<string> AddLanguageTags(BaseItem item, List<string> languages)
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

        // Log filtered out languages
        if (filteredOutLanguages.Count > 0)
        {
            _logger.LogInformation("Filtered out languages for {ItemName}: {Languages}", item.Name, string.Join(", ", filteredOutLanguages));
        }

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
