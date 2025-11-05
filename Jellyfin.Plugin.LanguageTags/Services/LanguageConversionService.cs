using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LanguageTags.Services;

/// <summary>
/// Service for converting between ISO language codes and language names.
/// </summary>
public class LanguageConversionService
{
    private readonly ILogger<LanguageConversionService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LanguageConversionService"/> class.
    /// </summary>
    /// <param name="logger">Instance of the logger.</param>
    public LanguageConversionService(ILogger<LanguageConversionService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Converts a language code to its 3-letter ISO 639-2 equivalent.
    /// If the input is already a 3-letter code, returns it as-is.
    /// If the input is a 2-letter ISO 639-1 code, converts it to the corresponding 3-letter code.
    /// </summary>
    /// <param name="languageCode">The language code to convert.</param>
    /// <returns>The 3-letter ISO 639-2 language code.</returns>
    public string ConvertToThreeLetterIsoCode(string languageCode)
    {
        if (languageCode.Length == 3)
        {
            return languageCode;
        }

        if (languageCode.Length == 2 && LanguageData.TryGetLanguageInfo(languageCode, out var languageInfo) && languageInfo != null)
        {
            return GetPreferredIsoCode(languageInfo, languageCode);
        }

        return languageCode;
    }

    /// <summary>
    /// Gets the preferred ISO code from language info, with fallback.
    /// </summary>
    /// <param name="languageInfo">The language info.</param>
    /// <param name="fallback">Fallback value if no ISO code found.</param>
    /// <returns>The preferred ISO code.</returns>
    private static string GetPreferredIsoCode(LanguageInfo languageInfo, string fallback)
    {
        return !string.IsNullOrEmpty(languageInfo.Iso6392) ? languageInfo.Iso6392 :
               !string.IsNullOrEmpty(languageInfo.Iso6392B) ? languageInfo.Iso6392B : fallback;
    }

    /// <summary>
    /// Converts a list of ISO codes to their corresponding language names.
    /// </summary>
    /// <param name="isoCodes">List of ISO language codes.</param>
    /// <returns>List of language names.</returns>
    public List<string> ConvertIsoToLanguageNames(List<string> isoCodes)
    {
        return isoCodes.Select(ConvertSingleIsoToLanguageName).ToList();
    }

    /// <summary>
    /// Converts a list of language names to their corresponding ISO codes.
    /// </summary>
    /// <param name="languageNames">List of language names.</param>
    /// <returns>List of ISO codes.</returns>
    public List<string> ConvertLanguageNamesToIso(List<string> languageNames)
    {
        return languageNames.Select(ConvertSingleLanguageNameToIso).ToList();
    }

    /// <summary>
    /// Converts a single ISO code to its corresponding language name.
    /// </summary>
    /// <param name="isoCode">The ISO language code.</param>
    /// <returns>The language name or the original code if not found.</returns>
    private string ConvertSingleIsoToLanguageName(string isoCode)
    {
        if (LanguageData.TryGetLanguageInfo(isoCode, out var languageInfo) &&
            languageInfo != null && !string.IsNullOrWhiteSpace(languageInfo.Name))
        {
            return languageInfo.Name;
        }

        _logger.LogWarning("Could not find language name for ISO code '{IsoCode}', using code as fallback", isoCode);
        return isoCode;
    }

    /// <summary>
    /// Converts a single language name to its corresponding ISO code.
    /// </summary>
    /// <param name="languageName">The language name.</param>
    /// <returns>The ISO code or the original name if not found.</returns>
    private string ConvertSingleLanguageNameToIso(string languageName)
    {
        var foundLanguage = LanguageData.LanguageDictionary.Values
            .FirstOrDefault(lang => lang.Name.Equals(languageName, StringComparison.OrdinalIgnoreCase));

        if (foundLanguage != null)
        {
            return GetPreferredIsoCode(foundLanguage, languageName);
        }

        // If it's already an ISO code (3 letters), keep it
        if (languageName.Length == 3)
        {
            return languageName;
        }

        _logger.LogWarning("Could not find ISO code for language name '{LanguageName}', using name as fallback", languageName);
        return languageName;
    }
}
