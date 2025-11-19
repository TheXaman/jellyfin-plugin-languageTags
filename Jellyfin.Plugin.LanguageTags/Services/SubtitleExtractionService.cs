using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MediaBrowser.Controller.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LanguageTags.Services;

/// <summary>
/// Service for extracting subtitle language information from external subtitle files.
/// </summary>
public class SubtitleExtractionService
{
    private readonly ILogger<SubtitleExtractionService> _logger;
    private readonly LanguageConversionService _conversionService;
    private static readonly Regex SubtitleLanguageRegex = new(@"\.(\w{2,3})\.", RegexOptions.Compiled);

    /// <summary>
    /// Initializes a new instance of the <see cref="SubtitleExtractionService"/> class.
    /// </summary>
    /// <param name="logger">Instance of the logger.</param>
    /// <param name="conversionService">Instance of the language conversion service.</param>
    public SubtitleExtractionService(
        ILogger<SubtitleExtractionService> logger,
        LanguageConversionService conversionService)
    {
        _logger = logger;
        _conversionService = conversionService;
    }

    /// <summary>
    /// Extracts subtitle languages from external subtitle files.
    /// </summary>
    /// <param name="video">The video to extract subtitle languages from.</param>
    /// <returns>List of subtitle language ISO codes.</returns>
    public List<string> ExtractSubtitleLanguagesExternal(Video video)
    {
        if (!video.HasSubtitles)
        {
            return new List<string>();
        }

        var subtitleLanguagesISO = video.SubtitleFiles
            .SelectMany(ExtractLanguageCodesFromFilename)
            .Distinct()
            .ToList();

        _logger.LogInformation(
            "Final external subtitle languages for {VideoName}: [{Languages}]",
            video.Name,
            string.Join(", ", subtitleLanguagesISO));

        return subtitleLanguagesISO;
    }

    /// <summary>
    /// Extracts language codes from a subtitle filename.
    /// </summary>
    /// <param name="subtitleFile">The subtitle filename.</param>
    /// <returns>List of extracted language ISO codes.</returns>
    private IEnumerable<string> ExtractLanguageCodesFromFilename(string subtitleFile)
    {
        return SubtitleLanguageRegex.Matches(subtitleFile)
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value.ToLowerInvariant())
            .Where(LanguageData.IsValidLanguageCode)
            .Select(_conversionService.ConvertToThreeLetterIsoCode);
    }
}
