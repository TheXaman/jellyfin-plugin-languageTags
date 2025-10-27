using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.LanguageTags;

/// <summary>
/// Static class containing ISO language code data and utilities.
/// </summary>
public static class LanguageData
{
    private static readonly Dictionary<string, LanguageInfo> _languageDictionary = InitializeLanguageDictionary();

    /// <summary>
    /// Gets the language dictionary for fast lookups.
    /// </summary>
    public static Dictionary<string, LanguageInfo> LanguageDictionary => _languageDictionary;

    /// <summary>
    /// Checks if a language code is valid (exists in the ISO standards).
    /// </summary>
    /// <param name="code">The language code to validate.</param>
    /// <returns>True if the code is a valid ISO language code, false otherwise.</returns>
    public static bool IsValidLanguageCode(string? code)
    {
        return !string.IsNullOrEmpty(code) && _languageDictionary.ContainsKey(code);
    }

    /// <summary>
    /// Tries to get language information for a given code.
    /// </summary>
    /// <param name="code">The language code to look up.</param>
    /// <param name="languageInfo">The language information if found.</param>
    /// <returns>True if the language was found, false otherwise.</returns>
    public static bool TryGetLanguageInfo(string? code, out LanguageInfo? languageInfo)
    {
        languageInfo = null;
        return !string.IsNullOrEmpty(code) && _languageDictionary.TryGetValue(code, out languageInfo);
    }

    private static Dictionary<string, LanguageInfo> InitializeLanguageDictionary()
    {
        var languages = new Dictionary<string, LanguageInfo>(StringComparer.OrdinalIgnoreCase);

        void AddLanguage(LanguageInfo lang)
        {
            if (!string.IsNullOrEmpty(lang.Iso6392))
            {
                languages[lang.Iso6392] = lang;
            }

            if (!string.IsNullOrEmpty(lang.Iso6392B))
            {
                languages[lang.Iso6392B] = lang;
            }

            if (!string.IsNullOrEmpty(lang.Iso6391))
            {
                languages[lang.Iso6391] = lang;
            }
        }

        // Add just one example language (German) as requested
        AddLanguage(new LanguageInfo { Iso6392 = "aar", Iso6391 = "aa", Name = "Afar" });
        AddLanguage(new LanguageInfo { Iso6392 = "abk", Iso6391 = "ab", Name = "Abkhazian" });
        AddLanguage(new LanguageInfo { Iso6392 = "ace", Name = "Achinese" });
        AddLanguage(new LanguageInfo { Iso6392 = "ach", Name = "Acoli" });
        AddLanguage(new LanguageInfo { Iso6392 = "ada", Name = "Adangme" });
        AddLanguage(new LanguageInfo { Iso6392 = "ady", Name = "Adyghe" });
        AddLanguage(new LanguageInfo { Iso6392 = "afr", Iso6391 = "af", Name = "Afrikaans" });
        AddLanguage(new LanguageInfo { Iso6392 = "ain", Name = "Ainu" });
        AddLanguage(new LanguageInfo { Iso6392 = "aka", Iso6391 = "ak", Name = "Akan" });
        AddLanguage(new LanguageInfo { Iso6392 = "alb", Iso6392B = "sqi", Iso6391 = "sq", Name = "Albanian" });
        AddLanguage(new LanguageInfo { Iso6392 = "ale", Name = "Aleut" });
        AddLanguage(new LanguageInfo { Iso6392 = "alt", Name = "Southern Altai" });
        AddLanguage(new LanguageInfo { Iso6392 = "amh", Iso6391 = "am", Name = "Amharic" });
        AddLanguage(new LanguageInfo { Iso6392 = "anp", Name = "Angika" });
        AddLanguage(new LanguageInfo { Iso6392 = "ara", Iso6391 = "ar", Name = "Arabic" });
        AddLanguage(new LanguageInfo { Iso6392 = "arg", Iso6391 = "an", Name = "Aragonese" });
        AddLanguage(new LanguageInfo { Iso6392 = "arm", Iso6392B = "hye", Iso6391 = "hy", Name = "Armenian" });
        AddLanguage(new LanguageInfo { Iso6392 = "arn", Name = "Mapudungun" });
        AddLanguage(new LanguageInfo { Iso6392 = "arp", Name = "Arapaho" });
        AddLanguage(new LanguageInfo { Iso6392 = "arw", Name = "Arawak" });
        AddLanguage(new LanguageInfo { Iso6392 = "asm", Iso6391 = "as", Name = "Assamese" });
        AddLanguage(new LanguageInfo { Iso6392 = "ast", Name = "Asturian" });
        AddLanguage(new LanguageInfo { Iso6392 = "ava", Iso6391 = "av", Name = "Avaric" });
        AddLanguage(new LanguageInfo { Iso6392 = "awa", Name = "Awadhi" });
        AddLanguage(new LanguageInfo { Iso6392 = "aym", Iso6391 = "ay", Name = "Aymara" });
        AddLanguage(new LanguageInfo { Iso6392 = "aze", Iso6391 = "az", Name = "Azerbaijani" });
        AddLanguage(new LanguageInfo { Iso6392 = "bak", Iso6391 = "ba", Name = "Bashkir" });
        AddLanguage(new LanguageInfo { Iso6392 = "bal", Name = "Baluchi" });
        AddLanguage(new LanguageInfo { Iso6392 = "bam", Iso6391 = "bm", Name = "Bambara" });
        AddLanguage(new LanguageInfo { Iso6392 = "ban", Name = "Balinese" });
        AddLanguage(new LanguageInfo { Iso6392 = "baq", Iso6392B = "eus", Iso6391 = "eu", Name = "Basque" });
        AddLanguage(new LanguageInfo { Iso6392 = "bas", Name = "Basa" });
        AddLanguage(new LanguageInfo { Iso6392 = "bej", Name = "Beja" });
        AddLanguage(new LanguageInfo { Iso6392 = "bel", Iso6391 = "be", Name = "Belarusian" });
        AddLanguage(new LanguageInfo { Iso6392 = "bem", Name = "Bemba" });
        AddLanguage(new LanguageInfo { Iso6392 = "ben", Iso6391 = "bn", Name = "Bengali" });
        AddLanguage(new LanguageInfo { Iso6392 = "bho", Name = "Bhojpuri" });
        AddLanguage(new LanguageInfo { Iso6392 = "bik", Name = "Bikol" });
        AddLanguage(new LanguageInfo { Iso6392 = "bin", Name = "Bini" });
        AddLanguage(new LanguageInfo { Iso6392 = "bis", Iso6391 = "bi", Name = "Bislama" });
        AddLanguage(new LanguageInfo { Iso6392 = "bla", Name = "Siksika" });
        AddLanguage(new LanguageInfo { Iso6392 = "bod", Iso6392B = "tib", Iso6391 = "bo", Name = "Tibetan" });
        AddLanguage(new LanguageInfo { Iso6392 = "bos", Iso6391 = "bs", Name = "Bosnian" });
        AddLanguage(new LanguageInfo { Iso6392 = "bra", Name = "Braj" });
        AddLanguage(new LanguageInfo { Iso6392 = "bre", Iso6391 = "br", Name = "Breton" });
        AddLanguage(new LanguageInfo { Iso6392 = "bua", Name = "Buriat" });
        AddLanguage(new LanguageInfo { Iso6392 = "bug", Name = "Buginese" });
        AddLanguage(new LanguageInfo { Iso6392 = "bul", Iso6391 = "bg", Name = "Bulgarian" });
        AddLanguage(new LanguageInfo { Iso6392 = "bur", Iso6392B = "mya", Iso6391 = "my", Name = "Burmese" });
        AddLanguage(new LanguageInfo { Iso6392 = "byn", Name = "Blin" });
        AddLanguage(new LanguageInfo { Iso6392 = "cad", Name = "Caddo" });
        AddLanguage(new LanguageInfo { Iso6392 = "car", Name = "Galibi Carib" });
        AddLanguage(new LanguageInfo { Iso6392 = "cat", Iso6391 = "ca", Name = "Catalan" });
        AddLanguage(new LanguageInfo { Iso6392 = "ceb", Name = "Cebuano" });
        AddLanguage(new LanguageInfo { Iso6392 = "ces", Iso6392B = "cze", Iso6391 = "cs", Name = "Czech" });
        AddLanguage(new LanguageInfo { Iso6392 = "cha", Iso6391 = "ch", Name = "Chamorro" });
        AddLanguage(new LanguageInfo { Iso6392 = "che", Iso6391 = "ce", Name = "Chechen" });
        AddLanguage(new LanguageInfo { Iso6392 = "chi", Iso6392B = "zho", Iso6391 = "zh", Name = "Chinese" });
        AddLanguage(new LanguageInfo { Iso6392 = "chk", Name = "Chuukese" });
        AddLanguage(new LanguageInfo { Iso6392 = "chm", Name = "Mari" });
        AddLanguage(new LanguageInfo { Iso6392 = "chn", Name = "Chinook jargon" });
        AddLanguage(new LanguageInfo { Iso6392 = "cho", Name = "Choctaw" });
        AddLanguage(new LanguageInfo { Iso6392 = "chp", Name = "Chipewyan" });
        AddLanguage(new LanguageInfo { Iso6392 = "chr", Name = "Cherokee" });
        AddLanguage(new LanguageInfo { Iso6392 = "chv", Iso6391 = "cv", Name = "Chuvash" });
        AddLanguage(new LanguageInfo { Iso6392 = "chy", Name = "Cheyenne" });
        AddLanguage(new LanguageInfo { Iso6392 = "cnr", Name = "Montenegrin" });
        AddLanguage(new LanguageInfo { Iso6392 = "cor", Iso6391 = "kw", Name = "Cornish" });
        AddLanguage(new LanguageInfo { Iso6392 = "cos", Iso6391 = "co", Name = "Corsican" });
        AddLanguage(new LanguageInfo { Iso6392 = "cre", Iso6391 = "cr", Name = "Cree" });
        AddLanguage(new LanguageInfo { Iso6392 = "crh", Name = "Crimean Tatar" });
        AddLanguage(new LanguageInfo { Iso6392 = "csb", Name = "Kashubian" });
        AddLanguage(new LanguageInfo { Iso6392 = "cym", Iso6392B = "wel", Iso6391 = "cy", Name = "Welsh" });
        AddLanguage(new LanguageInfo { Iso6392 = "dak", Name = "Dakota" });
        AddLanguage(new LanguageInfo { Iso6392 = "dan", Iso6391 = "da", Name = "Danish" });
        AddLanguage(new LanguageInfo { Iso6392 = "dar", Name = "Dargwa" });
        AddLanguage(new LanguageInfo { Iso6392 = "del", Name = "Delaware" });
        AddLanguage(new LanguageInfo { Iso6392 = "den", Name = "Slave (Athapascan)" });
        AddLanguage(new LanguageInfo { Iso6392 = "dgr", Name = "Dogrib" });
        AddLanguage(new LanguageInfo { Iso6392 = "din", Name = "Dinka" });
        AddLanguage(new LanguageInfo { Iso6392 = "div", Iso6391 = "dv", Name = "Divehi" });
        AddLanguage(new LanguageInfo { Iso6392 = "doi", Name = "Dogri" });
        AddLanguage(new LanguageInfo { Iso6392 = "dsb", Name = "Lower Sorbian" });
        AddLanguage(new LanguageInfo { Iso6392 = "dua", Name = "Duala" });
        AddLanguage(new LanguageInfo { Iso6392 = "dut", Iso6392B = "nld", Iso6391 = "nl", Name = "Dutch" });
        AddLanguage(new LanguageInfo { Iso6392 = "dyu", Name = "Dyula" });
        AddLanguage(new LanguageInfo { Iso6392 = "dzo", Iso6391 = "dz", Name = "Dzongkha" });
        AddLanguage(new LanguageInfo { Iso6392 = "efi", Name = "Efik" });
        AddLanguage(new LanguageInfo { Iso6392 = "eka", Name = "Ekajuk" });
        AddLanguage(new LanguageInfo { Iso6392 = "ell", Iso6392B = "gre", Iso6391 = "el", Name = "Greek Modern" });
        AddLanguage(new LanguageInfo { Iso6392 = "eng", Iso6391 = "en", Name = "English" });
        AddLanguage(new LanguageInfo { Iso6392 = "est", Iso6391 = "et", Name = "Estonian" });
        AddLanguage(new LanguageInfo { Iso6392 = "ewe", Iso6391 = "ee", Name = "Ewe" });
        AddLanguage(new LanguageInfo { Iso6392 = "ewo", Name = "Ewondo" });
        AddLanguage(new LanguageInfo { Iso6392 = "fan", Name = "Fang" });
        AddLanguage(new LanguageInfo { Iso6392 = "fao", Iso6391 = "fo", Name = "Faroese" });
        AddLanguage(new LanguageInfo { Iso6392 = "fas", Iso6392B = "per", Iso6391 = "fa", Name = "Persian" });
        AddLanguage(new LanguageInfo { Iso6392 = "fat", Name = "Fanti" });
        AddLanguage(new LanguageInfo { Iso6392 = "fij", Iso6391 = "fj", Name = "Fijian" });
        AddLanguage(new LanguageInfo { Iso6392 = "fil", Name = "Filipino" });
        AddLanguage(new LanguageInfo { Iso6392 = "fin", Iso6391 = "fi", Name = "Finnish" });
        AddLanguage(new LanguageInfo { Iso6392 = "fon", Name = "Fon" });
        AddLanguage(new LanguageInfo { Iso6392 = "fre", Iso6392B = "fra", Iso6391 = "fr", Name = "French" });
        AddLanguage(new LanguageInfo { Iso6392 = "frr", Name = "Northern Frisian" });
        AddLanguage(new LanguageInfo { Iso6392 = "frs", Name = "East Frisian Low Saxon" });
        AddLanguage(new LanguageInfo { Iso6392 = "fry", Iso6391 = "fy", Name = "Western Frisian" });
        AddLanguage(new LanguageInfo { Iso6392 = "ful", Iso6391 = "ff", Name = "Fulah" });
        AddLanguage(new LanguageInfo { Iso6392 = "fur", Name = "Friulian" });
        AddLanguage(new LanguageInfo { Iso6392 = "gaa", Name = "Ga" });
        AddLanguage(new LanguageInfo { Iso6392 = "gay", Name = "Gayo" });
        AddLanguage(new LanguageInfo { Iso6392 = "gba", Name = "Gbaya" });
        AddLanguage(new LanguageInfo { Iso6392 = "geo", Iso6392B = "kat", Iso6391 = "ka", Name = "Georgian" });
        AddLanguage(new LanguageInfo { Iso6392 = "ger", Iso6392B = "deu", Iso6391 = "de", Name = "German" });
        AddLanguage(new LanguageInfo { Iso6392 = "gil", Name = "Gilbertese" });
        AddLanguage(new LanguageInfo { Iso6392 = "gla", Iso6391 = "gd", Name = "Gaelic" });
        AddLanguage(new LanguageInfo { Iso6392 = "gle", Iso6391 = "ga", Name = "Irish" });
        AddLanguage(new LanguageInfo { Iso6392 = "glg", Iso6391 = "gl", Name = "Galician" });
        AddLanguage(new LanguageInfo { Iso6392 = "glv", Iso6391 = "gv", Name = "Manx" });
        AddLanguage(new LanguageInfo { Iso6392 = "gon", Name = "Gondi" });
        AddLanguage(new LanguageInfo { Iso6392 = "gor", Name = "Gorontalo" });
        AddLanguage(new LanguageInfo { Iso6392 = "grb", Name = "Grebo" });
        AddLanguage(new LanguageInfo { Iso6392 = "grn", Iso6391 = "gn", Name = "Guarani" });
        AddLanguage(new LanguageInfo { Iso6392 = "gsw", Name = "Swiss German" });
        AddLanguage(new LanguageInfo { Iso6392 = "guj", Iso6391 = "gu", Name = "Gujarati" });
        AddLanguage(new LanguageInfo { Iso6392 = "gwi", Name = "Gwich'in" });
        AddLanguage(new LanguageInfo { Iso6392 = "hai", Name = "Haida" });
        AddLanguage(new LanguageInfo { Iso6392 = "hat", Iso6391 = "ht", Name = "Haitian" });
        AddLanguage(new LanguageInfo { Iso6392 = "hau", Iso6391 = "ha", Name = "Hausa" });
        AddLanguage(new LanguageInfo { Iso6392 = "haw", Name = "Hawaiian" });
        AddLanguage(new LanguageInfo { Iso6392 = "heb", Iso6391 = "he", Name = "Hebrew" });
        AddLanguage(new LanguageInfo { Iso6392 = "her", Iso6391 = "hz", Name = "Herero" });
        AddLanguage(new LanguageInfo { Iso6392 = "hil", Name = "Hiligaynon" });
        AddLanguage(new LanguageInfo { Iso6392 = "hin", Iso6391 = "hi", Name = "Hindi" });
        AddLanguage(new LanguageInfo { Iso6392 = "hmn", Name = "Hmong" });
        AddLanguage(new LanguageInfo { Iso6392 = "hmo", Iso6391 = "ho", Name = "Hiri Motu" });
        AddLanguage(new LanguageInfo { Iso6392 = "hrv", Iso6391 = "hr", Name = "Croatian" });
        AddLanguage(new LanguageInfo { Iso6392 = "hsb", Name = "Upper Sorbian" });
        AddLanguage(new LanguageInfo { Iso6392 = "hun", Iso6391 = "hu", Name = "Hungarian" });
        AddLanguage(new LanguageInfo { Iso6392 = "hup", Name = "Hupa" });
        AddLanguage(new LanguageInfo { Iso6392 = "iba", Name = "Iban" });
        AddLanguage(new LanguageInfo { Iso6392 = "ibo", Iso6391 = "ig", Name = "Igbo" });
        AddLanguage(new LanguageInfo { Iso6392 = "iii", Iso6391 = "ii", Name = "Sichuan Yi" });
        AddLanguage(new LanguageInfo { Iso6392 = "iku", Iso6391 = "iu", Name = "Inuktitut" });
        AddLanguage(new LanguageInfo { Iso6392 = "ilo", Name = "Iloko" });
        AddLanguage(new LanguageInfo { Iso6392 = "ind", Iso6391 = "id", Name = "Indonesian" });
        AddLanguage(new LanguageInfo { Iso6392 = "inh", Name = "Ingush" });
        AddLanguage(new LanguageInfo { Iso6392 = "ipk", Iso6391 = "ik", Name = "Inupiaq" });
        AddLanguage(new LanguageInfo { Iso6392 = "isl", Iso6392B = "ice", Iso6391 = "is", Name = "Icelandic" });
        AddLanguage(new LanguageInfo { Iso6392 = "ita", Iso6391 = "it", Name = "Italian" });
        AddLanguage(new LanguageInfo { Iso6392 = "jav", Iso6391 = "jv", Name = "Javanese" });
        AddLanguage(new LanguageInfo { Iso6392 = "jpn", Iso6391 = "ja", Name = "Japanese" });
        AddLanguage(new LanguageInfo { Iso6392 = "jpr", Name = "Judeo-Persian" });
        AddLanguage(new LanguageInfo { Iso6392 = "jrb", Name = "Judeo-Arabic" });
        AddLanguage(new LanguageInfo { Iso6392 = "kaa", Name = "Kara-Kalpak" });
        AddLanguage(new LanguageInfo { Iso6392 = "kab", Name = "Kabyle" });
        AddLanguage(new LanguageInfo { Iso6392 = "kac", Name = "Kachin" });
        AddLanguage(new LanguageInfo { Iso6392 = "kal", Iso6391 = "kl", Name = "Kalaallisut" });
        AddLanguage(new LanguageInfo { Iso6392 = "kam", Name = "Kamba" });
        AddLanguage(new LanguageInfo { Iso6392 = "kan", Iso6391 = "kn", Name = "Kannada" });
        AddLanguage(new LanguageInfo { Iso6392 = "kas", Iso6391 = "ks", Name = "Kashmiri" });
        AddLanguage(new LanguageInfo { Iso6392 = "kau", Iso6391 = "kr", Name = "Kanuri" });
        AddLanguage(new LanguageInfo { Iso6392 = "kaz", Iso6391 = "kk", Name = "Kazakh" });
        AddLanguage(new LanguageInfo { Iso6392 = "kbd", Name = "Kabardian" });
        AddLanguage(new LanguageInfo { Iso6392 = "kha", Name = "Khasi" });
        AddLanguage(new LanguageInfo { Iso6392 = "khm", Iso6391 = "km", Name = "Central Khmer" });
        AddLanguage(new LanguageInfo { Iso6392 = "kik", Iso6391 = "ki", Name = "Kikuyu" });
        AddLanguage(new LanguageInfo { Iso6392 = "kin", Iso6391 = "rw", Name = "Kinyarwanda" });
        AddLanguage(new LanguageInfo { Iso6392 = "kir", Iso6391 = "ky", Name = "Kirghiz" });
        AddLanguage(new LanguageInfo { Iso6392 = "kmb", Name = "Kimbundu" });
        AddLanguage(new LanguageInfo { Iso6392 = "kok", Name = "Konkani" });
        AddLanguage(new LanguageInfo { Iso6392 = "kom", Iso6391 = "kv", Name = "Komi" });
        AddLanguage(new LanguageInfo { Iso6392 = "kon", Iso6391 = "kg", Name = "Kongo" });
        AddLanguage(new LanguageInfo { Iso6392 = "kor", Iso6391 = "ko", Name = "Korean" });
        AddLanguage(new LanguageInfo { Iso6392 = "kos", Name = "Kosraean" });
        AddLanguage(new LanguageInfo { Iso6392 = "kpe", Name = "Kpelle" });
        AddLanguage(new LanguageInfo { Iso6392 = "krc", Name = "Karachay-Balkar" });
        AddLanguage(new LanguageInfo { Iso6392 = "krl", Name = "Karelian" });
        AddLanguage(new LanguageInfo { Iso6392 = "kru", Name = "Kurukh" });
        AddLanguage(new LanguageInfo { Iso6392 = "kua", Iso6391 = "kj", Name = "Kuanyama" });
        AddLanguage(new LanguageInfo { Iso6392 = "kum", Name = "Kumyk" });
        AddLanguage(new LanguageInfo { Iso6392 = "kur", Iso6391 = "ku", Name = "Kurdish" });
        AddLanguage(new LanguageInfo { Iso6392 = "kut", Name = "Kutenai" });
        AddLanguage(new LanguageInfo { Iso6392 = "lad", Name = "Ladino" });
        AddLanguage(new LanguageInfo { Iso6392 = "lah", Name = "Lahnda" });
        AddLanguage(new LanguageInfo { Iso6392 = "lam", Name = "Lamba" });
        AddLanguage(new LanguageInfo { Iso6392 = "lao", Iso6391 = "lo", Name = "Lao" });
        AddLanguage(new LanguageInfo { Iso6392 = "lav", Iso6391 = "lv", Name = "Latvian" });
        AddLanguage(new LanguageInfo { Iso6392 = "lez", Name = "Lezghian" });
        AddLanguage(new LanguageInfo { Iso6392 = "lim", Iso6391 = "li", Name = "Limburgan" });
        AddLanguage(new LanguageInfo { Iso6392 = "lin", Iso6391 = "ln", Name = "Lingala" });
        AddLanguage(new LanguageInfo { Iso6392 = "lit", Iso6391 = "lt", Name = "Lithuanian" });
        AddLanguage(new LanguageInfo { Iso6392 = "lol", Name = "Mongo" });
        AddLanguage(new LanguageInfo { Iso6392 = "loz", Name = "Lozi" });
        AddLanguage(new LanguageInfo { Iso6392 = "ltz", Iso6391 = "lb", Name = "Luxembourgish" });
        AddLanguage(new LanguageInfo { Iso6392 = "lua", Name = "Luba-Lulua" });
        AddLanguage(new LanguageInfo { Iso6392 = "lub", Iso6391 = "lu", Name = "Luba-Katanga" });
        AddLanguage(new LanguageInfo { Iso6392 = "lug", Iso6391 = "lg", Name = "Ganda" });
        AddLanguage(new LanguageInfo { Iso6392 = "lun", Name = "Lunda" });
        AddLanguage(new LanguageInfo { Iso6392 = "luo", Name = "Luo (Kenya and Tanzania)" });
        AddLanguage(new LanguageInfo { Iso6392 = "lus", Name = "Lushai" });
        AddLanguage(new LanguageInfo { Iso6392 = "mac", Iso6392B = "mkd", Iso6391 = "mk", Name = "Macedonian" });
        AddLanguage(new LanguageInfo { Iso6392 = "mad", Name = "Madurese" });
        AddLanguage(new LanguageInfo { Iso6392 = "mag", Name = "Magahi" });
        AddLanguage(new LanguageInfo { Iso6392 = "mah", Iso6391 = "mh", Name = "Marshallese" });
        AddLanguage(new LanguageInfo { Iso6392 = "mai", Name = "Maithili" });
        AddLanguage(new LanguageInfo { Iso6392 = "mak", Name = "Makasar" });
        AddLanguage(new LanguageInfo { Iso6392 = "mal", Iso6391 = "ml", Name = "Malayalam" });
        AddLanguage(new LanguageInfo { Iso6392 = "man", Name = "Mandingo" });
        AddLanguage(new LanguageInfo { Iso6392 = "mao", Iso6392B = "mri", Iso6391 = "mi", Name = "Maori" });
        AddLanguage(new LanguageInfo { Iso6392 = "mar", Iso6391 = "mr", Name = "Marathi" });
        AddLanguage(new LanguageInfo { Iso6392 = "mas", Name = "Masai" });
        AddLanguage(new LanguageInfo { Iso6392 = "may", Iso6392B = "msa", Iso6391 = "ms", Name = "Malay" });
        AddLanguage(new LanguageInfo { Iso6392 = "mdf", Name = "Moksha" });
        AddLanguage(new LanguageInfo { Iso6392 = "mdr", Name = "Mandar" });
        AddLanguage(new LanguageInfo { Iso6392 = "men", Name = "Mende" });
        AddLanguage(new LanguageInfo { Iso6392 = "mic", Name = "Mi'kmaq" });
        AddLanguage(new LanguageInfo { Iso6392 = "min", Name = "Minangkabau" });
        AddLanguage(new LanguageInfo { Iso6392 = "mlg", Iso6391 = "mg", Name = "Malagasy" });
        AddLanguage(new LanguageInfo { Iso6392 = "mlt", Iso6391 = "mt", Name = "Maltese" });
        AddLanguage(new LanguageInfo { Iso6392 = "mnc", Name = "Manchu" });
        AddLanguage(new LanguageInfo { Iso6392 = "mni", Name = "Manipuri" });
        AddLanguage(new LanguageInfo { Iso6392 = "moh", Name = "Mohawk" });
        AddLanguage(new LanguageInfo { Iso6392 = "mon", Iso6391 = "mn", Name = "Mongolian" });
        AddLanguage(new LanguageInfo { Iso6392 = "mos", Name = "Mossi" });
        AddLanguage(new LanguageInfo { Iso6392 = "mwl", Name = "Mirandese" });
        AddLanguage(new LanguageInfo { Iso6392 = "mwr", Name = "Marwari" });
        AddLanguage(new LanguageInfo { Iso6392 = "myv", Name = "Erzya" });
        AddLanguage(new LanguageInfo { Iso6392 = "nap", Name = "Neapolitan" });
        AddLanguage(new LanguageInfo { Iso6392 = "nau", Iso6391 = "na", Name = "Nauru" });
        AddLanguage(new LanguageInfo { Iso6392 = "nav", Iso6391 = "nv", Name = "Navajo" });
        AddLanguage(new LanguageInfo { Iso6392 = "nbl", Iso6391 = "nr", Name = "Ndebele South" });
        AddLanguage(new LanguageInfo { Iso6392 = "nde", Iso6391 = "nd", Name = "Ndebele North" });
        AddLanguage(new LanguageInfo { Iso6392 = "ndo", Iso6391 = "ng", Name = "Ndonga" });
        AddLanguage(new LanguageInfo { Iso6392 = "nds", Name = "Low German" });
        AddLanguage(new LanguageInfo { Iso6392 = "nep", Iso6391 = "ne", Name = "Nepali" });
        AddLanguage(new LanguageInfo { Iso6392 = "new", Name = "Nepal Bhasa" });
        AddLanguage(new LanguageInfo { Iso6392 = "nia", Name = "Nias" });
        AddLanguage(new LanguageInfo { Iso6392 = "niu", Name = "Niuean" });
        AddLanguage(new LanguageInfo { Iso6392 = "nno", Iso6391 = "nn", Name = "Norwegian Nynorsk" });
        AddLanguage(new LanguageInfo { Iso6392 = "nob", Iso6391 = "nb", Name = "Bokmål Norwegian" });
        AddLanguage(new LanguageInfo { Iso6392 = "nog", Name = "Nogai" });
        AddLanguage(new LanguageInfo { Iso6392 = "nor", Iso6391 = "no", Name = "Norwegian" });
        AddLanguage(new LanguageInfo { Iso6392 = "nqo", Name = "N'Ko" });
        AddLanguage(new LanguageInfo { Iso6392 = "nso", Name = "Pedi" });
        AddLanguage(new LanguageInfo { Iso6392 = "nya", Iso6391 = "ny", Name = "Chichewa" });
        AddLanguage(new LanguageInfo { Iso6392 = "nym", Name = "Nyamwezi" });
        AddLanguage(new LanguageInfo { Iso6392 = "nyn", Name = "Nyankole" });
        AddLanguage(new LanguageInfo { Iso6392 = "nyo", Name = "Nyoro" });
        AddLanguage(new LanguageInfo { Iso6392 = "nzi", Name = "Nzima" });
        AddLanguage(new LanguageInfo { Iso6392 = "oci", Iso6391 = "oc", Name = "Occitan" });
        AddLanguage(new LanguageInfo { Iso6392 = "oji", Iso6391 = "oj", Name = "Ojibwa" });
        AddLanguage(new LanguageInfo { Iso6392 = "ori", Iso6391 = "or", Name = "Oriya" });
        AddLanguage(new LanguageInfo { Iso6392 = "orm", Iso6391 = "om", Name = "Oromo" });
        AddLanguage(new LanguageInfo { Iso6392 = "osa", Name = "Osage" });
        AddLanguage(new LanguageInfo { Iso6392 = "oss", Iso6391 = "os", Name = "Ossetian" });
        AddLanguage(new LanguageInfo { Iso6392 = "pag", Name = "Pangasinan" });
        AddLanguage(new LanguageInfo { Iso6392 = "pam", Name = "Pampanga" });
        AddLanguage(new LanguageInfo { Iso6392 = "pan", Iso6391 = "pa", Name = "Panjabi" });
        AddLanguage(new LanguageInfo { Iso6392 = "pap", Name = "Papiamento" });
        AddLanguage(new LanguageInfo { Iso6392 = "pau", Name = "Palauan" });
        AddLanguage(new LanguageInfo { Iso6392 = "pol", Iso6391 = "pl", Name = "Polish" });
        AddLanguage(new LanguageInfo { Iso6392 = "pon", Name = "Pohnpeian" });
        AddLanguage(new LanguageInfo { Iso6392 = "por", Iso6391 = "pt", Name = "Portuguese" });
        AddLanguage(new LanguageInfo { Iso6392 = "pus", Iso6391 = "ps", Name = "Pushto" });
        AddLanguage(new LanguageInfo { Iso6392 = "que", Iso6391 = "qu", Name = "Quechua" });
        AddLanguage(new LanguageInfo { Iso6392 = "raj", Name = "Rajasthani" });
        AddLanguage(new LanguageInfo { Iso6392 = "rap", Name = "Rapanui" });
        AddLanguage(new LanguageInfo { Iso6392 = "rar", Name = "Rarotongan" });
        AddLanguage(new LanguageInfo { Iso6392 = "roh", Iso6391 = "rm", Name = "Romansh" });
        AddLanguage(new LanguageInfo { Iso6392 = "rom", Name = "Romany" });
        AddLanguage(new LanguageInfo { Iso6392 = "rum", Iso6392B = "ron", Iso6391 = "ro", Name = "Romanian" });
        AddLanguage(new LanguageInfo { Iso6392 = "run", Iso6391 = "rn", Name = "Rundi" });
        AddLanguage(new LanguageInfo { Iso6392 = "rup", Name = "Aromanian" });
        AddLanguage(new LanguageInfo { Iso6392 = "rus", Iso6391 = "ru", Name = "Russian" });
        AddLanguage(new LanguageInfo { Iso6392 = "sad", Name = "Sandawe" });
        AddLanguage(new LanguageInfo { Iso6392 = "sag", Iso6391 = "sg", Name = "Sango" });
        AddLanguage(new LanguageInfo { Iso6392 = "sah", Name = "Yakut" });
        AddLanguage(new LanguageInfo { Iso6392 = "sas", Name = "Sasak" });
        AddLanguage(new LanguageInfo { Iso6392 = "sat", Name = "Santali" });
        AddLanguage(new LanguageInfo { Iso6392 = "scn", Name = "Sicilian" });
        AddLanguage(new LanguageInfo { Iso6392 = "sco", Name = "Scots" });
        AddLanguage(new LanguageInfo { Iso6392 = "sel", Name = "Selkup" });
        AddLanguage(new LanguageInfo { Iso6392 = "shn", Name = "Shan" });
        AddLanguage(new LanguageInfo { Iso6392 = "sid", Name = "Sidamo" });
        AddLanguage(new LanguageInfo { Iso6392 = "sin", Iso6391 = "si", Name = "Sinhala" });
        AddLanguage(new LanguageInfo { Iso6392 = "slo", Iso6392B = "slk", Iso6391 = "sk", Name = "Slovak" });
        AddLanguage(new LanguageInfo { Iso6392 = "slv", Iso6391 = "sl", Name = "Slovenian" });
        AddLanguage(new LanguageInfo { Iso6392 = "sma", Name = "Southern Sami" });
        AddLanguage(new LanguageInfo { Iso6392 = "sme", Iso6391 = "se", Name = "Northern Sami" });
        AddLanguage(new LanguageInfo { Iso6392 = "smj", Name = "Lule Sami" });
        AddLanguage(new LanguageInfo { Iso6392 = "smn", Name = "Inari Sami" });
        AddLanguage(new LanguageInfo { Iso6392 = "smo", Iso6391 = "sm", Name = "Samoan" });
        AddLanguage(new LanguageInfo { Iso6392 = "sms", Name = "Skolt Sami" });
        AddLanguage(new LanguageInfo { Iso6392 = "sna", Iso6391 = "sn", Name = "Shona" });
        AddLanguage(new LanguageInfo { Iso6392 = "snd", Iso6391 = "sd", Name = "Sindhi" });
        AddLanguage(new LanguageInfo { Iso6392 = "snk", Name = "Soninke" });
        AddLanguage(new LanguageInfo { Iso6392 = "som", Iso6391 = "so", Name = "Somali" });
        AddLanguage(new LanguageInfo { Iso6392 = "sot", Iso6391 = "st", Name = "Sotho Southern" });
        AddLanguage(new LanguageInfo { Iso6392 = "spa", Iso6391 = "es", Name = "Spanish" });
        AddLanguage(new LanguageInfo { Iso6392 = "srd", Iso6391 = "sc", Name = "Sardinian" });
        AddLanguage(new LanguageInfo { Iso6392 = "srn", Name = "Sranan Tongo" });
        AddLanguage(new LanguageInfo { Iso6392 = "srp", Iso6391 = "sr", Name = "Serbian" });
        AddLanguage(new LanguageInfo { Iso6392 = "srr", Name = "Serer" });
        AddLanguage(new LanguageInfo { Iso6392 = "ssw", Iso6391 = "ss", Name = "Swati" });
        AddLanguage(new LanguageInfo { Iso6392 = "suk", Name = "Sukuma" });
        AddLanguage(new LanguageInfo { Iso6392 = "sun", Iso6391 = "su", Name = "Sundanese" });
        AddLanguage(new LanguageInfo { Iso6392 = "sus", Name = "Susu" });
        AddLanguage(new LanguageInfo { Iso6392 = "swa", Iso6391 = "sw", Name = "Swahili" });
        AddLanguage(new LanguageInfo { Iso6392 = "swe", Iso6391 = "sv", Name = "Swedish" });
        AddLanguage(new LanguageInfo { Iso6392 = "syr", Name = "Syriac" });
        AddLanguage(new LanguageInfo { Iso6392 = "tah", Iso6391 = "ty", Name = "Tahitian" });
        AddLanguage(new LanguageInfo { Iso6392 = "tam", Iso6391 = "ta", Name = "Tamil" });
        AddLanguage(new LanguageInfo { Iso6392 = "tat", Iso6391 = "tt", Name = "Tatar" });
        AddLanguage(new LanguageInfo { Iso6392 = "tel", Iso6391 = "te", Name = "Telugu" });
        AddLanguage(new LanguageInfo { Iso6392 = "tem", Name = "Timne" });
        AddLanguage(new LanguageInfo { Iso6392 = "ter", Name = "Tereno" });
        AddLanguage(new LanguageInfo { Iso6392 = "tet", Name = "Tetum" });
        AddLanguage(new LanguageInfo { Iso6392 = "tgk", Iso6391 = "tg", Name = "Tajik" });
        AddLanguage(new LanguageInfo { Iso6392 = "tgl", Iso6391 = "tl", Name = "Tagalog" });
        AddLanguage(new LanguageInfo { Iso6392 = "tha", Iso6391 = "th", Name = "Thai" });
        AddLanguage(new LanguageInfo { Iso6392 = "tig", Name = "Tigre" });
        AddLanguage(new LanguageInfo { Iso6392 = "tir", Iso6391 = "ti", Name = "Tigrinya" });
        AddLanguage(new LanguageInfo { Iso6392 = "tiv", Name = "Tiv" });
        AddLanguage(new LanguageInfo { Iso6392 = "tkl", Name = "Tokelau" });
        AddLanguage(new LanguageInfo { Iso6392 = "tli", Name = "Tlingit" });
        AddLanguage(new LanguageInfo { Iso6392 = "tmh", Name = "Tamashek" });
        AddLanguage(new LanguageInfo { Iso6392 = "tog", Name = "Tonga (Nyasa)" });
        AddLanguage(new LanguageInfo { Iso6392 = "ton", Iso6391 = "to", Name = "Tonga (Tonga Islands)" });
        AddLanguage(new LanguageInfo { Iso6392 = "tpi", Name = "Tok Pisin" });
        AddLanguage(new LanguageInfo { Iso6392 = "tsi", Name = "Tsimshian" });
        AddLanguage(new LanguageInfo { Iso6392 = "tsn", Iso6391 = "tn", Name = "Tswana" });
        AddLanguage(new LanguageInfo { Iso6392 = "tso", Iso6391 = "ts", Name = "Tsonga" });
        AddLanguage(new LanguageInfo { Iso6392 = "tuk", Iso6391 = "tk", Name = "Turkmen" });
        AddLanguage(new LanguageInfo { Iso6392 = "tum", Name = "Tumbuka" });
        AddLanguage(new LanguageInfo { Iso6392 = "tur", Iso6391 = "tr", Name = "Turkish" });
        AddLanguage(new LanguageInfo { Iso6392 = "tvl", Name = "Tuvalu" });
        AddLanguage(new LanguageInfo { Iso6392 = "twi", Iso6391 = "tw", Name = "Twi" });
        AddLanguage(new LanguageInfo { Iso6392 = "tyv", Name = "Tuvinian" });
        AddLanguage(new LanguageInfo { Iso6392 = "udm", Name = "Udmurt" });
        AddLanguage(new LanguageInfo { Iso6392 = "uig", Iso6391 = "ug", Name = "Uighur" });
        AddLanguage(new LanguageInfo { Iso6392 = "ukr", Iso6391 = "uk", Name = "Ukrainian" });
        AddLanguage(new LanguageInfo { Iso6392 = "umb", Name = "Umbundu" });
        AddLanguage(new LanguageInfo { Iso6392 = "urd", Iso6391 = "ur", Name = "Urdu" });
        AddLanguage(new LanguageInfo { Iso6392 = "uzb", Iso6391 = "uz", Name = "Uzbek" });
        AddLanguage(new LanguageInfo { Iso6392 = "vai", Name = "Vai" });
        AddLanguage(new LanguageInfo { Iso6392 = "ven", Iso6391 = "ve", Name = "Venda" });
        AddLanguage(new LanguageInfo { Iso6392 = "vie", Iso6391 = "vi", Name = "Vietnamese" });
        AddLanguage(new LanguageInfo { Iso6392 = "vot", Name = "Votic" });
        AddLanguage(new LanguageInfo { Iso6392 = "wal", Name = "Walamo" });
        AddLanguage(new LanguageInfo { Iso6392 = "war", Name = "Waray" });
        AddLanguage(new LanguageInfo { Iso6392 = "was", Name = "Washo" });
        AddLanguage(new LanguageInfo { Iso6392 = "wln", Iso6391 = "wa", Name = "Walloon" });
        AddLanguage(new LanguageInfo { Iso6392 = "wol", Iso6391 = "wo", Name = "Wolof" });
        AddLanguage(new LanguageInfo { Iso6392 = "xal", Name = "Kalmyk" });
        AddLanguage(new LanguageInfo { Iso6392 = "xho", Iso6391 = "xh", Name = "Xhosa" });
        AddLanguage(new LanguageInfo { Iso6392 = "yao", Name = "Yao" });
        AddLanguage(new LanguageInfo { Iso6392 = "yap", Name = "Yapese" });
        AddLanguage(new LanguageInfo { Iso6392 = "yid", Iso6391 = "yi", Name = "Yiddish" });
        AddLanguage(new LanguageInfo { Iso6392 = "yor", Iso6391 = "yo", Name = "Yoruba" });
        AddLanguage(new LanguageInfo { Iso6392 = "zap", Name = "Zapotec" });
        AddLanguage(new LanguageInfo { Iso6392 = "zen", Name = "Zenaga" });
        AddLanguage(new LanguageInfo { Iso6392 = "zgh", Name = "Standard Moroccan Tamazight" });
        AddLanguage(new LanguageInfo { Iso6392 = "zha", Iso6391 = "za", Name = "Zhuang" });
        AddLanguage(new LanguageInfo { Iso6392 = "zul", Iso6391 = "zu", Name = "Zulu" });
        AddLanguage(new LanguageInfo { Iso6392 = "zun", Name = "Zuni" });
        AddLanguage(new LanguageInfo { Iso6392 = "zza", Name = "Zaza" });

        // Special codes
        AddLanguage(new LanguageInfo { Iso6392 = "und", Name = "Undetermined" });
        AddLanguage(new LanguageInfo { Iso6392 = "mul", Name = "Multiple languages" });
        AddLanguage(new LanguageInfo { Iso6392 = "zxx", Name = "No linguistic content" });
        AddLanguage(new LanguageInfo { Iso6392 = "mis", Name = "Uncoded languages" });

        return languages;
    }
}
