using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LanguageTags.ScheduledTasks;

/// <summary>
/// Class representing a task to refresh library for new language tags.
/// </summary>
public class RefreshLibraryTask : IScheduledTask, IDisposable
{
    private readonly ILogger<RefreshLibraryTask> _logger;
    private readonly LanguageTagsManager _languageTagsManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="RefreshLibraryTask"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="collectionManager">Instance of the <see cref="ICollectionManager"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{RefreshLibraryTask}"/> interface.</param>
    /// <param name="boxsetLogger">Instance of the <see cref="ILogger{LanguageTagsManager}"/> interface.</param>
    public RefreshLibraryTask(
        ILibraryManager libraryManager,
        ICollectionManager collectionManager,
        ILogger<RefreshLibraryTask> logger,
        ILogger<LanguageTagsManager> boxsetLogger)
    {
        _logger = logger;
        _languageTagsManager = new LanguageTagsManager(libraryManager, collectionManager, boxsetLogger);
    }

    /// <inheritdoc/>
    public string Name => "Scan library for new language tags";

    /// <inheritdoc/>
    public string Key => "LanguageTagsSetsRefreshLibraryTask";

    /// <inheritdoc/>
    public string Description => "Scans all items in the library for new language tags.";

    /// <inheritdoc/>
    public string Category => "Language Tags";

    /// <inheritdoc/>
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting LanguageTags refresh library task");
        await _languageTagsManager.ScanLibrary(false).ConfigureAwait(false);
        _logger.LogInformation("LanguageTags refresh library task finished");
    }

    /// <inheritdoc/>
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // Run this task every 24 hours
        return [new TaskTriggerInfo { Type = TaskTriggerInfo.TriggerInterval, IntervalTicks = TimeSpan.FromHours(24).Ticks }];
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases unmanaged and - optionally - managed resources.
    /// </summary>
    /// <param name="dispose"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
    protected virtual void Dispose(bool dispose)
    {
        if (dispose)
        {
            _languageTagsManager.Dispose();
        }
    }
}