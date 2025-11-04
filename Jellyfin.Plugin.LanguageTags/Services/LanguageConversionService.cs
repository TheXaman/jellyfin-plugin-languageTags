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
    /// Converts a list of ISO codes to their corresponding language names.
    /// </summary>
    /// <param name="isoCodes">List of ISO language codes.</param>
    /// <returns>List of language names.</returns>
    public List<string> ConvertIsoToLanguageNames(List<string> isoCodes)
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

    /// <summary>
    /// Converts a list of language names to their corresponding ISO codes.
    /// </summary>
    /// <param name="languageNames">List of language names.</param>
    /// <returns>List of ISO codes.</returns>
    public List<string> ConvertLanguageNamesToIso(List<string> languageNames)
    {
        var isoCodes = new List<string>();

        foreach (var languageName in languageNames)
        {
            // Try to find the language by name by searching through all entries
            LanguageInfo? foundLanguage = null;
            foreach (var kvp in LanguageData.LanguageDictionary)
            {
                if (kvp.Value.Name.Equals(languageName, StringComparison.OrdinalIgnoreCase))
                {
                    foundLanguage = kvp.Value;
                    break;
                }
            }

            if (foundLanguage != null)
            {
                // Prefer Iso6392 (3-letter code), but fall back to Iso6392B if needed
                var isoCode = !string.IsNullOrEmpty(foundLanguage.Iso6392) ? foundLanguage.Iso6392 :
                              !string.IsNullOrEmpty(foundLanguage.Iso6392B) ? foundLanguage.Iso6392B : languageName;
                isoCodes.Add(isoCode);
            }
            else
            {
                // If it's already an ISO code (3 letters), keep it
                if (languageName.Length == 3)
                {
                    isoCodes.Add(languageName);
                }
                else
                {
                    // Fallback to the original value if we can't convert it
                    _logger.LogWarning("Could not find ISO code for language name '{LanguageName}', using name as fallback", languageName);
                    isoCodes.Add(languageName);
                }
            }
        }

        return isoCodes;
    }
}
