using System;
using System.Net.Mime;
using System.Threading.Tasks;
using MediaBrowser.Controller.Collections;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LanguageTags.Api;

/// <summary>
/// The language tags Api controller.
/// </summary>
[ApiController]
[Authorize]
[Route("[controller]")]
[Produces(MediaTypeNames.Application.Json)]
public class LanguageTagsController : ControllerBase, IDisposable
{
    private readonly LanguageTagsManager _languageTagsManager;
    private readonly ILogger<LanguageTagsController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LanguageTagsController"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="collectionManager">Instance of the <see cref="ICollectionManager"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{LanguageTagsController}"/> interface.</param>
    /// <param name="languageTagsLogger">Instance of the <see cref="ILogger{LanguageTagsManager}"/> interface.</param>
    public LanguageTagsController(
        ILibraryManager libraryManager,
        ICollectionManager collectionManager,
        ILogger<LanguageTagsController> logger,
        ILogger<LanguageTagsManager> languageTagsLogger)
    {
        _languageTagsManager = new LanguageTagsManager(libraryManager, collectionManager, languageTagsLogger);
        _logger = logger;
    }

    /// <summary>
    /// Starts a manual FULL refresh of language tags.
    /// </summary>
    /// <response code="204">Library scan and language tagging started successfully. </response>
    /// <returns>A <see cref="NoContentResult"/> indicating success.</returns>
    [HttpPost("RefreshLanguageTags")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> RefreshMetadataRequest()
    {
        _logger.LogInformation("Starting a manual refresh of language tags");
        await _languageTagsManager.ScanLibrary(true).ConfigureAwait(false);
        _logger.LogInformation("Completed refresh of language tags");
        return NoContent();
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
