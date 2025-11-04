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
                        var threeLetterCode = _conversionService.ConvertToThreeLetterIsoCode(languageCode);
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
}
