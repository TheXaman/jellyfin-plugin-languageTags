using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.LanguageTags.Configuration;

/// <summary>
/// Class holding the plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration" /> class.
    /// </summary>
    public PluginConfiguration()
    {
        AlwaysForceFullRefresh = false;
        WhitelistLanguageTags = string.Empty;
        AddSubtitleTags = false;
        SynchronousRefresh = false;
        DisableUndefinedLanguageTags = false;
    }

    /// <summary>
    /// Gets or sets a value indicating whether to always force a full refresh.
    /// </summary>
    public bool AlwaysForceFullRefresh { get; set; }

    /// <summary>
    /// Gets or sets the whitelist of language tags.
    /// </summary>
    public string WhitelistLanguageTags { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to extract subtitle languages.
    /// </summary>
    public bool AddSubtitleTags { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to refresh synchronously.
    /// </summary>
    public bool SynchronousRefresh { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to disable adding 'und' tags for undefined languages.
    /// </summary>
    public bool DisableUndefinedLanguageTags { get; set; }
}
