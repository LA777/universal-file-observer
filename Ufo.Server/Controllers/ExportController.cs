using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ufo.Extensions;
using Ufo.Server.Attributes;
using Ufo.Server.Services;

namespace Ufo.Server.Controllers;

/// <summary>
/// The Settings page's Export section: the caller's data as a zip archive
/// holding one JSON file, in one of four scopes.
/// </summary>
/// <remarks>
/// GET rather than POST: an export changes nothing and may be fetched again
/// for the same answer. Everything is scoped to the user in the token; there
/// is no way through here to another account's data. The archive is named
/// with the scope and the UTC moment it was taken, so two exports in a row do
/// not overwrite one another in a downloads folder.
/// </remarks>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[JwtClaimsRequired]
public class ExportController : ControllerBase
{
    private const string ZipContentType = "application/zip";

    private readonly ILogger<ExportController> _logger;
    private readonly IExportService _exportService;

    public ExportController(IExportService exportService, ILogger<ExportController> logger)
    {
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The account with its settings, shortcuts and locked folder tabs.</summary>
    [HttpGet("user")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, ZipContentType)]
    public Task<IActionResult> ExportUserAsync(CancellationToken cancellationToken) =>
        ExportAsync(ExportScope.User, cancellationToken);

    /// <summary>Flags, ratings and tags on files and folders on disk.</summary>
    [HttpGet("filesystem")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, ZipContentType)]
    public Task<IActionResult> ExportFileSystemAsync(CancellationToken cancellationToken) =>
        ExportAsync(ExportScope.FileSystem, cancellationToken);

    /// <summary>Every snapshot with its whole tree, and the labels.</summary>
    [HttpGet("snapshots")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, ZipContentType)]
    public Task<IActionResult> ExportSnapshotsAsync(CancellationToken cancellationToken) =>
        ExportAsync(ExportScope.Snapshots, cancellationToken);

    /// <summary>All three in one file.</summary>
    [HttpGet("all")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, ZipContentType)]
    public Task<IActionResult> ExportAllAsync(CancellationToken cancellationToken) =>
        ExportAsync(ExportScope.All, cancellationToken);

    private async Task<IActionResult> ExportAsync(ExportScope scope, CancellationToken cancellationToken)
    {
        _logger.LogInformation("ExportAsync - Scope: {Scope}", scope);
        var userId = HttpContext.GetUserIdAsUlid();

        var archive = await _exportService.ExportAsync(scope, userId, cancellationToken);

        return File(archive.Content, ZipContentType, archive.FileName);
    }
}
