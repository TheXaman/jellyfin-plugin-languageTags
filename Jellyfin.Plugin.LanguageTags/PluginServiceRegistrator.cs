using Jellyfin.Plugin.LanguageTags.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.LanguageTags;

/// <summary>
/// Register plugin service.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc/>
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // Register services
        serviceCollection.AddSingleton<ConfigurationService>();
        serviceCollection.AddSingleton<LanguageConversionService>();
        serviceCollection.AddSingleton<LanguageTagService>();
        serviceCollection.AddSingleton<LibraryQueryService>();
        serviceCollection.AddSingleton<SubtitleExtractionService>();

        // Register LanguageTagsManager as both singleton and hosted service
        serviceCollection.AddSingleton<LanguageTagsManager>();
        serviceCollection.AddHostedService(provider => provider.GetRequiredService<LanguageTagsManager>());
    }
}
