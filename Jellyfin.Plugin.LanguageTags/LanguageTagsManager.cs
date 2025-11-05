using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Extensions;
using Jellyfin.Plugin.LanguageTags.Services;
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
public sealed class LanguageTagsManager : IHostedService, IDisposable
{
    private readonly ILibraryManager _libraryManager;
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
    /// <param name="logger">Instance of the <see cref="ILogger{LanguageTagsManager}"/> interface.</param>
    /// <param name="configService">Instance of the configuration service.</param>
    /// <param name="conversionService">Instance of the language conversion service.</param>
    /// <param name="tagService">Instance of the language tag service.</param>
    /// <param name="queryService">Instance of the library query service.</param>
    /// <param name="subtitleService">Instance of the subtitle extraction service.</param>
    public LanguageTagsManager(
        ILibraryManager libraryManager,
        ILogger<LanguageTagsManager> logger,
        ConfigurationService configService,
        LanguageConversionService conversionService,
        LanguageTagService tagService,
        LibraryQueryService queryService,
        SubtitleExtractionService subtitleService)
    {
        _libraryManager = libraryManager;
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
    /// <param name="getExistingTags">Whether to get existing tags or not.</param>
    /// <param name="scanContext">Scan context with prefixes and configuration.</param>
    /// <returns>Tuple indicating if video should be processed and any existing languages found.</returns>
    private (bool ShouldProcess, List<string> ExistingAudio, List<string> ExistingSubtitle) CheckAndPrepareVideoForProcessing(
        Video video, bool fullScan, bool subtitleTags, bool getExistingTags, (string AudioPrefix, string SubtitlePrefix, List<string> Whitelist, bool DisableUndefinedTags) scanContext)
    {
        bool shouldProcess = fullScan;
        var existingAudio = new List<string>();
        var existingSubtitle = new List<string>();

        if (fullScan)
        {
            _tagService.RemoveLanguageTags(video, TagType.Audio, scanContext.AudioPrefix, scanContext.SubtitlePrefix);
            if (subtitleTags)
            {
                _tagService.RemoveLanguageTags(video, TagType.Subtitle, scanContext.AudioPrefix, scanContext.SubtitlePrefix);
            }

            shouldProcess = true;
            return (shouldProcess, existingAudio, existingSubtitle);
        }

        // Check audio tags
        var hasAudioTags = _tagService.HasLanguageTags(video, TagType.Audio, scanContext.AudioPrefix, scanContext.SubtitlePrefix);
        if (hasAudioTags)
        {
            if (getExistingTags)
            {
                var audioNames = _tagService.StripTagPrefix(_tagService.GetLanguageTags(video, TagType.Audio, scanContext.AudioPrefix, scanContext.SubtitlePrefix), TagType.Audio, scanContext.AudioPrefix, scanContext.SubtitlePrefix);
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
            var hasSubtitleTags = _tagService.HasLanguageTags(video, TagType.Subtitle, scanContext.AudioPrefix, scanContext.SubtitlePrefix);
            if (hasSubtitleTags)
            {
                if (getExistingTags)
                {
                    var subtitleNames = _tagService.StripTagPrefix(_tagService.GetLanguageTags(video, TagType.Subtitle, scanContext.AudioPrefix, scanContext.SubtitlePrefix), TagType.Subtitle, scanContext.AudioPrefix, scanContext.SubtitlePrefix);
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

        // Get prefixes and whitelist once at the start to avoid repeated queries
        var audioPrefix = _configService.GetAudioLanguageTagPrefix();
        var subtitlePrefix = _configService.GetSubtitleLanguageTagPrefix();
        var whitelist = LanguageTagService.ParseWhitelist(_configService.WhitelistLanguageTags);
        var disableUndefinedTags = _configService.DisableUndefinedLanguageTags;

        _logger.LogInformation(
            "Scan configuration - Audio prefix: '{AudioPrefix}', Subtitle prefix: '{SubtitlePrefix}', Whitelist: {WhitelistCount} codes ({Whitelist})",
            audioPrefix,
            subtitlePrefix,
            whitelist.Count,
            whitelist.Count > 0 ? string.Join(", ", whitelist) : "none");

        LogScanConfiguration(fullScan, synchronously, subtitleTags);

        // Create scan context to pass parameters
        var scanContext = (audioPrefix, subtitlePrefix, whitelist, disableUndefinedTags);

        // Process the libraries
        switch (type.ToLowerInvariant())
        {
            case "movies":
                await ProcessLibraryMovies(fullScan, synchronously, subtitleTags, scanContext).ConfigureAwait(false);
                break;
            case "series":
            case "tvshows":
                await ProcessLibrarySeries(fullScan, synchronously, subtitleTags, scanContext).ConfigureAwait(false);
                break;
            case "collections":
                await ProcessLibraryCollections(fullScan, synchronously, subtitleTags, scanContext).ConfigureAwait(false);
                break;
            default:
                await ProcessAllLibraryTypes(fullScan, synchronously, subtitleTags, scanContext).ConfigureAwait(false);
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
    private async Task ProcessAllLibraryTypes(bool fullScan, bool synchronously, bool subtitleTags, (string AudioPrefix, string SubtitlePrefix, List<string> Whitelist, bool DisableUndefinedTags) scanContext)
    {
        await ProcessLibraryMovies(fullScan, synchronously, subtitleTags, scanContext).ConfigureAwait(false);
        await ProcessLibrarySeries(fullScan, synchronously, subtitleTags, scanContext).ConfigureAwait(false);
        await ProcessLibraryCollections(fullScan, synchronously, subtitleTags, scanContext).ConfigureAwait(false);
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

        var audioPrefix = _configService.GetAudioLanguageTagPrefix();
        var subtitlePrefix = _configService.GetSubtitleLanguageTagPrefix();

        foreach (var item in items)
        {
            _tagService.RemoveLanguageTags(item, TagType.Audio, audioPrefix, subtitlePrefix);
            _tagService.RemoveLanguageTags(item, TagType.Subtitle, audioPrefix, subtitlePrefix);
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
    /// <param name="scanContext">Scan context containing prefixes and whitelist.</param>
    /// <returns>A <see cref="Task"/> representing the library scan progress.</returns>
    private async Task ProcessLibraryMovies(bool fullScan, bool synchronously, bool subtitleTags, (string AudioPrefix, string SubtitlePrefix, List<string> Whitelist, bool DisableUndefinedTags) scanContext)
    {
        LogProcessingHeader("Processing movies...");

        var movies = _queryService.GetMoviesFromLibrary();
        var (moviesProcessed, moviesSkipped) = await ProcessItemsAsync(
            movies,
            async (movie, ct) => await ProcessMovie(movie, fullScan, subtitleTags, scanContext, ct).ConfigureAwait(false),
            synchronously).ConfigureAwait(false);

        _logger.LogInformation(
            "MOVIES - processed {Processed} of {Total} ({Skipped} skipped)",
            moviesProcessed,
            movies.Count,
            moviesSkipped);
    }

    private async Task<bool> ProcessMovie(Movie movie, bool fullScan, bool subtitleTags, (string AudioPrefix, string SubtitlePrefix, List<string> Whitelist, bool DisableUndefinedTags) scanContext, CancellationToken cancellationToken)
    {
        if (movie is not Video video)
        {
            return false;
        }

        var (shouldProcess, _, _) = CheckAndPrepareVideoForProcessing(video, fullScan, subtitleTags, false, scanContext);

        if (shouldProcess)
        {
            var (audioLanguages, subtitleLanguages) = await ProcessVideo(video, subtitleTags, scanContext, cancellationToken).ConfigureAwait(false);

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
    /// <param name="scanContext">Scan context containing prefixes and whitelist.</param>
    /// <returns>A <see cref="Task"/> representing the library scan progress.</returns>
    private async Task ProcessLibrarySeries(bool fullScan, bool synchronously, bool subtitleTags, (string AudioPrefix, string SubtitlePrefix, List<string> Whitelist, bool DisableUndefinedTags) scanContext)
    {
        LogProcessingHeader("Processing series...");

        var seriesList = _queryService.GetSeriesFromLibrary();
        var (processedSeries, skippedSeries) = await ProcessItemsAsync(
            seriesList,
            async (seriesBaseItem, ct) =>
            {
                if (seriesBaseItem is Series series)
                {
                    await ProcessSeries(series, fullScan, subtitleTags, scanContext, ct).ConfigureAwait(false);
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

    /// <summary>
    /// Processes a single episode, returning languages and whether it was processed.
    /// </summary>
    /// <param name="episode">The episode to process.</param>
    /// <param name="fullScan">Whether this is a full scan.</param>
    /// <param name="subtitleTags">Whether subtitle processing is enabled.</param>
    /// <param name="scanContext">Scan context containing prefixes and whitelist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tuple of (audio languages, subtitle languages, was processed).</returns>
    private async Task<(List<string> Audio, List<string> Subtitles, bool WasProcessed)> ProcessEpisode(
        Episode episode,
        bool fullScan,
        bool subtitleTags,
        (string AudioPrefix, string SubtitlePrefix, List<string> Whitelist, bool DisableUndefinedTags) scanContext,
        CancellationToken cancellationToken)
    {
        if (episode is not Video video)
        {
            return (new List<string>(), new List<string>(), false);
        }

        var (shouldProcess, existingAudio, existingSubtitle) =
            CheckAndPrepareVideoForProcessing(video, fullScan, subtitleTags, true, scanContext);

        if (shouldProcess)
        {
            var (audioLanguages, subtitleLanguages) =
                await ProcessVideo(video, subtitleTags, scanContext, cancellationToken).ConfigureAwait(false);

            // Save episode to repository
            await episode.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken)
                .ConfigureAwait(false);

            return (audioLanguages, subtitleLanguages, true);
        }

        return (existingAudio, existingSubtitle, false);
    }

    /// <summary>
    /// Processes all episodes in a season and applies aggregated tags.
    /// </summary>
    /// <param name="season">The season to process.</param>
    /// <param name="series">The parent series.</param>
    /// <param name="fullScan">Whether this is a full scan.</param>
    /// <param name="subtitleTags">Whether subtitle processing is enabled.</param>
    /// <param name="scanContext">Scan context containing prefixes and whitelist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tuple of (audio languages, subtitle languages).</returns>
    private async Task<(List<string> Audio, List<string> Subtitles)> ProcessSeason(
        Season season,
        Series series,
        bool fullScan,
        bool subtitleTags,
        (string AudioPrefix, string SubtitlePrefix, List<string> Whitelist, bool DisableUndefinedTags) scanContext,
        CancellationToken cancellationToken)
    {
        var episodes = _queryService.GetEpisodesFromSeason(season);

        if (episodes == null || episodes.Count == 0)
        {
            _logger.LogWarning(
                "No episodes found in SEASON {SeasonName} of {SeriesName}",
                season.Name,
                series.Name);
            return (new List<string>(), new List<string>());
        }

        _logger.LogDebug("Processing SEASON {SeasonName} of {SeriesName}", season.Name, series.Name);

        var seasonAudioLanguages = new List<string>();
        var seasonSubtitleLanguages = new List<string>();
        int episodesProcessed = 0;
        int episodesSkipped = 0;

        // Process each episode
        foreach (var episode in episodes)
        {
            var (audio, subtitles, wasProcessed) =
                await ProcessEpisode(episode, fullScan, subtitleTags, scanContext, cancellationToken)
                    .ConfigureAwait(false);

            seasonAudioLanguages.AddRange(audio);
            seasonSubtitleLanguages.AddRange(subtitles);

            if (wasProcessed)
            {
                episodesProcessed++;
            }
            else
            {
                episodesSkipped++;
            }
        }

        // Add audio tags to season (languages are already converted from episodes)
        if (seasonAudioLanguages.Count > 0)
        {
            seasonAudioLanguages = await Task.Run(
                () => _tagService.AddLanguageTags(season, seasonAudioLanguages, TagType.Audio, convertFromIso: false, scanContext.AudioPrefix, scanContext.SubtitlePrefix, scanContext.Whitelist),
                cancellationToken).ConfigureAwait(false);
        }

        // Add subtitle tags to season if enabled
        if (seasonSubtitleLanguages.Count > 0 && subtitleTags)
        {
            seasonSubtitleLanguages = await Task.Run(
                () => _tagService.AddLanguageTags(season, seasonSubtitleLanguages, TagType.Subtitle, convertFromIso: false, scanContext.AudioPrefix, scanContext.SubtitlePrefix, scanContext.Whitelist),
                cancellationToken).ConfigureAwait(false);
        }

        // Log season-level summary
        if (episodesProcessed > 0 && (seasonAudioLanguages.Count > 0 || seasonSubtitleLanguages.Count > 0))
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

        // Save season to repository
        if (seasonAudioLanguages.Count > 0 || (seasonSubtitleLanguages.Count > 0 && subtitleTags))
        {
            await season.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken)
                .ConfigureAwait(false);
        }

        return (seasonAudioLanguages, seasonSubtitleLanguages);
    }

    private async Task ProcessSeries(Series series, bool fullScan, bool subtitleTags, (string AudioPrefix, string SubtitlePrefix, List<string> Whitelist, bool DisableUndefinedTags) scanContext, CancellationToken cancellationToken)
    {
        var seasons = _queryService.GetSeasonsFromSeries(series);
        if (seasons == null || seasons.Count == 0)
        {
            _logger.LogWarning("No seasons found in SERIES {SeriesName}", series.Name);
            return;
        }

        var seriesAudioLanguages = new List<string>();
        var seriesSubtitleLanguages = new List<string>();

        // Process all seasons and aggregate languages
        foreach (var season in seasons)
        {
            var (seasonAudio, seasonSubtitles) = await ProcessSeason(
                season, series, fullScan, subtitleTags, scanContext, cancellationToken)
                .ConfigureAwait(false);

            seriesAudioLanguages.AddRange(seasonAudio);
            seriesSubtitleLanguages.AddRange(seasonSubtitles);
        }

        // Add audio tags to series (languages are already converted from seasons)
        if (seriesAudioLanguages.Count > 0)
        {
            seriesAudioLanguages = await Task.Run(
                () => _tagService.AddLanguageTags(series, seriesAudioLanguages, TagType.Audio, convertFromIso: false, scanContext.AudioPrefix, scanContext.SubtitlePrefix, scanContext.Whitelist),
                cancellationToken).ConfigureAwait(false);
        }

        // Add subtitle tags to series if enabled
        if (seriesSubtitleLanguages.Count > 0 && subtitleTags)
        {
            seriesSubtitleLanguages = await Task.Run(
                () => _tagService.AddLanguageTags(series, seriesSubtitleLanguages, TagType.Subtitle, convertFromIso: false, scanContext.AudioPrefix, scanContext.SubtitlePrefix, scanContext.Whitelist),
                cancellationToken).ConfigureAwait(false);
        }

        // Log series-level summary
        if (seriesAudioLanguages.Count > 0 || seriesSubtitleLanguages.Count > 0)
        {
            _logger.LogInformation(
                "SERIES - {SeriesName} - audio: {Audio} - subtitles: {Subtitles}",
                series.Name,
                seriesAudioLanguages.Count > 0 ? string.Join(", ", seriesAudioLanguages) : "none",
                seriesSubtitleLanguages.Count > 0 ? string.Join(", ", seriesSubtitleLanguages) : "none");
        }

        // Save series to repository
        if (seriesAudioLanguages.Count > 0 || (seriesSubtitleLanguages.Count > 0 && subtitleTags))
        {
            await series.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Processes the libraries collections.
    /// </summary>
    /// <param name="fullScan">if set to <c>true</c> [full scan].</param>
    /// <param name="synchronously">if set to <c>true</c> [synchronously].</param>
    /// <param name="subtitleTags">if set to <c>true</c> [extract subtitle languages].</param>
    /// <param name="scanContext">Scan context containing prefixes and whitelist.</param>
    /// <returns>A <see cref="Task"/> representing the library scan progress.</returns>
    private async Task ProcessLibraryCollections(bool fullScan, bool synchronously, bool subtitleTags, (string AudioPrefix, string SubtitlePrefix, List<string> Whitelist, bool DisableUndefinedTags) scanContext)
    {
        LogProcessingHeader("Processing collections...");

        var collections = _queryService.GetBoxSetsFromLibrary();
        var (collectionsProcessed, collectionsSkipped) = await ProcessItemsAsync(
            collections,
            async (collection, ct) => await ProcessCollection(collection, fullScan, subtitleTags, scanContext, ct).ConfigureAwait(false),
            synchronously).ConfigureAwait(false);

        _logger.LogInformation(
            "COLLECTIONS - processed {Processed} of {Total} ({Skipped} skipped)",
            collectionsProcessed,
            collections.Count,
            collectionsSkipped);
    }

    private async Task<bool> ProcessCollection(BoxSet collection, bool fullRefresh, bool subtitleTags, (string AudioPrefix, string SubtitlePrefix, List<string> Whitelist, bool DisableUndefinedTags) scanContext, CancellationToken cancellationToken)
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

            var movieLanguages = _tagService.GetLanguageTags(movie, TagType.Audio, scanContext.AudioPrefix, scanContext.SubtitlePrefix);
            collectionAudioLanguages.AddRange(movieLanguages);

            var movieSubtitleLanguages = _tagService.GetLanguageTags(movie, TagType.Subtitle, scanContext.AudioPrefix, scanContext.SubtitlePrefix);
            collectionSubtitleLanguages.AddRange(movieSubtitleLanguages);
        }

        // Strip audio language prefix
        collectionAudioLanguages = _tagService.StripTagPrefix(collectionAudioLanguages, TagType.Audio, scanContext.AudioPrefix, scanContext.SubtitlePrefix);

        // Add language tags to the box set
        var addedAudioLanguages = await Task.Run(() => _tagService.AddLanguageTags(collection, collectionAudioLanguages, TagType.Audio, convertFromIso: false, scanContext.AudioPrefix, scanContext.SubtitlePrefix, scanContext.Whitelist), cancellationToken).ConfigureAwait(false);

        // Strip subtitle language prefix
        collectionSubtitleLanguages = _tagService.StripTagPrefix(collectionSubtitleLanguages, TagType.Subtitle, scanContext.AudioPrefix, scanContext.SubtitlePrefix);

        // Add subtitle language tags to the box set
        List<string> addedSubtitleLanguages = new List<string>();
        if (subtitleTags && collectionSubtitleLanguages.Count > 0)
        {
            addedSubtitleLanguages = await Task.Run(() => _tagService.AddLanguageTags(collection, collectionSubtitleLanguages, TagType.Subtitle, convertFromIso: false, scanContext.AudioPrefix, scanContext.SubtitlePrefix, scanContext.Whitelist), cancellationToken).ConfigureAwait(false);
        }

        // Save collection to repository only once after all tag modifications
        // Only log if new tags were actually added
        if (addedAudioLanguages.Count > 0 || addedSubtitleLanguages.Count > 0)
        {
            await collection.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "COLLECTION - {CollectionName} - audio: {Audio} - subtitles: {Subtitles}",
                collection.Name,
                addedAudioLanguages.Count > 0 ? string.Join(", ", addedAudioLanguages) : "none",
                addedSubtitleLanguages.Count > 0 ? string.Join(", ", addedSubtitleLanguages) : "none");
            return true;
        }

        return false;
    }

    private async Task<(List<string> AudioLanguages, List<string> SubtitleLanguages)> ProcessVideo(Video video, bool subtitleTags, (string AudioPrefix, string SubtitlePrefix, List<string> Whitelist, bool DisableUndefinedTags) scanContext, CancellationToken cancellationToken)
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
                audioLanguages = await _tagService.AddAudioLanguageTagsOrUndefined(video, audioLanguages, scanContext.AudioPrefix, scanContext.SubtitlePrefix, scanContext.Whitelist, scanContext.DisableUndefinedTags, cancellationToken).ConfigureAwait(false);
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

            if (audioLanguages.Count > 0)
            {
                // Add audio language tags
                audioLanguages = await Task.Run(() => _tagService.AddLanguageTags(video, audioLanguages, TagType.Audio, convertFromIso: true, scanContext.AudioPrefix, scanContext.SubtitlePrefix, scanContext.Whitelist), cancellationToken).ConfigureAwait(false);
                _logger.LogDebug("Added audio tags for VIDEO {VideoName}: {AudioLanguages}", video.Name, string.Join(", ", audioLanguages));
            }
            else
            {
                audioLanguages = await _tagService.AddAudioLanguageTagsOrUndefined(video, audioLanguages, scanContext.AudioPrefix, scanContext.SubtitlePrefix, scanContext.Whitelist, scanContext.DisableUndefinedTags, cancellationToken).ConfigureAwait(false);
            }

            if (subtitleTags && subtitleLanguages.Count > 0)
            {
                // Add subtitle language tags
                subtitleLanguages = await Task.Run(() => _tagService.AddLanguageTags(video, subtitleLanguages, TagType.Subtitle, convertFromIso: true, scanContext.AudioPrefix, scanContext.SubtitlePrefix, scanContext.Whitelist), cancellationToken).ConfigureAwait(false);
                _logger.LogDebug("Added subtitle tags for VIDEO {VideoName}: {SubtitleLanguages}", video.Name, string.Join(", ", subtitleLanguages));
            }
            else if (subtitleTags)
            {
                _logger.LogWarning("No subtitle information found for VIDEO {VideoName}", video.Name);
            }

            // Save video to repository only once after all tag modifications
            await video.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
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
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
