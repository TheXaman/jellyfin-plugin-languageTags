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
    /// <returns>A <see cref="Task"/> representing the library scan progress.</returns>
    public async Task ScanLibrary(bool fullScan = false)
    {
        // Get configuration value for AlwaysForceFullRefresh
        var alwaysForceFullRefresh = Plugin.Instance?.Configuration?.AlwaysForceFullRefresh ?? false;
        fullScan = fullScan || alwaysForceFullRefresh;

        // Process movies
        await ProcessLibraryMovies(fullScan).ConfigureAwait(false);

        // Process series
        await ProcessLibrarySeries(fullScan).ConfigureAwait(false);

        // Process box sets / collections
        await ProcessLibraryCollections(fullScan).ConfigureAwait(false);
    }

    /// <summary>
    /// Processes the library movies.
    /// </summary>
    /// <param name="fullScan">if set to <c>true</c> [full scan].</param>
    /// <returns>A <see cref="Task"/> representing the library scan progress.</returns>
    private async Task ProcessLibraryMovies(bool fullScan)
    {
        _logger.LogInformation("****************************");
        _logger.LogInformation("*    Processing movies...  *");
        _logger.LogInformation("****************************");

        // Fetch all movies in the library
        var movies = GetMoviesFromLibrary();
        int totalMovies = movies.Count;
        int processedMovies = 0;

        await Parallel.ForEachAsync(movies, async (item, cancellationToken) =>
        {
            if (item is Video video)
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
                        _logger.LogInformation("Language tags already exist for {VideoName}", video.Name);
                        return;
                    }

                    RemoveLanguageTags(item);
                }

                await ProcessVideo(video, cancellationToken).ConfigureAwait(false);
            }

            Interlocked.Increment(ref processedMovies);
        }).ConfigureAwait(false);

        _logger.LogInformation("Processed {ProcessedMovies} of {TotalMovies} movies", processedMovies, totalMovies);
    }

    /// <summary>
    /// Processes the library series.
    /// </summary>
    /// <param name="fullScan">if set to <c>true</c> [full scan].</param>
    /// <returns>A <see cref="Task"/> representing the library scan progress.</returns>
    private async Task ProcessLibrarySeries(bool fullScan)
    {
        _logger.LogInformation("****************************");
        _logger.LogInformation("*    Processing series...  *");
        _logger.LogInformation("****************************");

        // Fetch all series in the library
        var seriesList = GetSeriesFromLibrary();
        var totalSeries = seriesList.Count;
        var processedSeries = 0;

        // Process each series
        await Parallel.ForEachAsync(seriesList, async (seriesBaseItem, cancellationToken) =>
        {
            // Check if the series is a valid series
            var series = seriesBaseItem as Series;
            if (series == null)
            {
                _logger.LogWarning("Series is null!");
                return;
            }

            // Get all seasons in the series
            var seasons = GetSeasonsFromSeries(series);
            if (seasons == null || seasons.Count == 0)
            {
                _logger.LogWarning("No seasons found in series {SeriesName}", series.Name);
                return;
            }

            // Get language tags from all episodes in the series
            var seriesLanguages = new List<string>();
            foreach (var season in seasons)
            {
                var episodes = GetEpisodesFromSeason(season);

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
                                _logger.LogInformation("Language tags already exist for {VideoName}", video.Name);
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
                    await Task.Run(() => AddLanguageTags(season, seasonLanguages), cancellationToken).ConfigureAwait(false);
                    _logger.LogInformation("Added tags for {SeasonName}: {Languages}", season.Name, string.Join(", ", seasonLanguages));
                }
                else
                {
                    await Task.Run(() => AddLanguageTags(season, new List<string> { "und" }), cancellationToken).ConfigureAwait(false);
                    _logger.LogInformation("No language information found for {SeasonName}", season.Name);
                }
            }

            // Remove existing language tags
            RemoveLanguageTags(series);

            // Add language tags to the series
            seriesLanguages = seriesLanguages.Distinct().ToList();
            if (seriesLanguages.Count > 0)
            {
                await Task.Run(() => AddLanguageTags(series, seriesLanguages), cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Added tags for {SeriesName}: {Languages}", series.Name, string.Join(", ", seriesLanguages));
            }
            else
            {
                await Task.Run(() => AddLanguageTags(series, new List<string> { "und" }), cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("No language information found for {SeriesName}", series.Name);
            }

            Interlocked.Increment(ref processedSeries);
        }).ConfigureAwait(false);

        _logger.LogInformation("Processed {ProcessedSeries} of {TotalSeries} series", processedSeries, totalSeries);
    }

    /// <summary>
    /// Processes the library collections.
    /// </summary>
    /// <param name="fullScan">if set to <c>true</c> [full scan].</param>
    /// <returns>A <see cref="Task"/> representing the library scan progress.</returns>
    private async Task ProcessLibraryCollections(bool fullScan)
    {
        _logger.LogInformation("******************************");
        _logger.LogInformation("*  Processing collections... *");
        _logger.LogInformation("******************************");

        // Fetch all box sets in the library
        var collections = GetBoxSetsFromLibrary();
        int totalCollections = collections.Count;

        await Parallel.ForEachAsync(collections, async (item, cancellationToken) =>
        {
            if (item is BoxSet boxSet)
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
                RemoveLanguageTags(item);

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
                    await Task.Run(() => AddLanguageTags(boxSet, collectionLanguages), cancellationToken).ConfigureAwait(false);
                    _logger.LogInformation("Added tags for {BoxSetName}: {Languages}", boxSet.Name, string.Join(", ", collectionLanguages));
                }
                else
                {
                    await Task.Run(() => AddLanguageTags(boxSet, new List<string> { "und" }), cancellationToken).ConfigureAwait(false);
                    _logger.LogInformation("No language information found for {BoxSetName}", boxSet.Name);
                }
            }
        }).ConfigureAwait(false);

        _logger.LogInformation("Processed {TotalCollections} collections", totalCollections);
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
        var languages = new List<string>();
        try
        {
            // Step 1: Run FFmpeg
            string ffmpegOutput = await Task.Run(() => RunFFmpeg(video.Path)).ConfigureAwait(false);

            // Step 2: Extract languages
            languages = await Task.Run(() => ExtractLanguages(ffmpegOutput)).ConfigureAwait(false);

            // Filter out tags that are not ISO 639-2/B language codes
            languages = languages.Where(lang => lang.Length == 3).ToList();

            if (languages.Count > 0)
            {
                // Step 2.5: Remove duplicates
                languages = languages.Distinct().ToList();

                // Step 3: Add language tags
                await Task.Run(() => AddLanguageTags(video, languages), cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Added tags for {VideoName}: {Languages}", video.Name, string.Join(", ", languages));
            }
            else
            {
                await Task.Run(() => AddLanguageTags(video, new List<string> { "und" }), cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("No language information found for {VideoName}", video.Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing {VideoName}", video.Name);
        }

        return languages;
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

    private void AddLanguageTags(BaseItem item, List<string> languages)
    {
        // Get the whitelist of language tags
        var whitelist = Plugin.Instance?.Configuration?.WhitelistLanguageTags ?? string.Empty;
        var whitelistArray = whitelist.Split(Separator, StringSplitOptions.RemoveEmptyEntries).Select(lang => lang.Trim()).ToList();
        // Remmove duplicates
        whitelistArray = whitelistArray.Distinct().ToList();
        // Remove invalid tags (not ISO 639-2/B language codes)
        whitelistArray = whitelistArray.Where(lang => lang.Length == 3).ToList();

        // Filter out tags that are not in the whitelist
        languages = languages.Where(lang => whitelistArray.Contains(lang)).ToList();

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
