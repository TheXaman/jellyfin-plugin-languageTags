using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LanguageTags.Services;

/// <summary>
/// Service for querying items from the Jellyfin library.
/// </summary>
public class LibraryQueryService
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<LibraryQueryService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryQueryService"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the library manager.</param>
    /// <param name="logger">Instance of the logger.</param>
    public LibraryQueryService(ILibraryManager libraryManager, ILogger<LibraryQueryService> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>
    /// Gets all movies from the library.
    /// </summary>
    /// <returns>List of movies.</returns>
    public List<Movie> GetMoviesFromLibrary()
    {
        return _libraryManager.QueryItems(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Movie],
            IsVirtualItem = false,
        }).Items.OfType<Movie>().ToList();
    }

    /// <summary>
    /// Gets all series from the library.
    /// </summary>
    /// <returns>List of series.</returns>
    public List<Series> GetSeriesFromLibrary()
    {
        return _libraryManager.QueryItems(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Series],
            Recursive = true,
        }).Items.OfType<Series>().ToList();
    }

    /// <summary>
    /// Gets all box sets from the library.
    /// </summary>
    /// <returns>List of box sets.</returns>
    public List<BoxSet> GetBoxSetsFromLibrary()
    {
        return _libraryManager.QueryItems(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.BoxSet],
            CollapseBoxSetItems = false,
            Recursive = true,
            HasTmdbId = true
        }).Items.OfType<BoxSet>().ToList();
    }

    /// <summary>
    /// Gets all seasons from a series.
    /// </summary>
    /// <param name="series">The series to get seasons from.</param>
    /// <returns>List of seasons.</returns>
    public List<Season> GetSeasonsFromSeries(Series series)
    {
        var seasons = _libraryManager.QueryItems(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Season],
            Recursive = true,
            ParentId = series.Id,
            IsVirtualItem = false
        }).Items.OfType<Season>().ToList();

        return seasons;
    }

    /// <summary>
    /// Gets all episodes from a season.
    /// </summary>
    /// <param name="season">The season to get episodes from.</param>
    /// <returns>List of episodes.</returns>
    public List<Episode> GetEpisodesFromSeason(Season season)
    {
        // Try primary query by ParentId first (works for most cases)
        var episodes = _libraryManager.QueryItems(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Episode],
            Recursive = true,
            ParentId = season.Id,
            IsVirtualItem = false
        }).Items.OfType<Episode>().ToList();

        if (episodes.Count == 0)
        {
            // Fallback: Some series (especially those with special characters) have episodes
            // with ParentId pointing to Series instead of Season. Query by SeriesId and season number.
            _logger.LogInformation(
                "No episodes found by ParentId for SEASON '{SeasonName}' of {SeriesName}, trying SeriesId-based query",
                season.Name,
                season.SeriesName);

            episodes = _libraryManager.QueryItems(new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.Episode],
                Recursive = true
            }).Items.OfType<Episode>()
            .Where(e => e.SeriesId == season.SeriesId && e.ParentIndexNumber == season.IndexNumber)
            .ToList();

            if (episodes.Count > 0)
            {
                _logger.LogInformation(
                    "Found {EpisodeCount} episodes for SEASON '{SeasonName}' of {SeriesName} using SeriesId fallback query",
                    episodes.Count,
                    season.Name,
                    season.SeriesName);
            }
        }

        if (episodes.Count == 0)
        {
            _logger.LogWarning(
                "No episodes found in SEASON '{SeasonName}' of '{SeriesName}'",
                season.Name,
                season.SeriesName);
        }

        return episodes;
    }
}
