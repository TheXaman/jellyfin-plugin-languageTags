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
    }

    /// <summary>
    /// Gets or sets a value indicating whether to always force a full refresh.
    /// </summary>
    public bool AlwaysForceFullRefresh { get; set; }
}
