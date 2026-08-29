using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;
using Ufo.Server.Services;

namespace Ufo.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class VideoController : ControllerBase
{
    // TODO LA - Cover with Functional tests
    private readonly IPathGuard _pathGuard;

    public VideoController(IPathGuard pathGuard)
    {
        _pathGuard = pathGuard ?? throw new ArgumentNullException(nameof(pathGuard));
    }

    [HttpGet()]
    public IActionResult GetVideo([FromQuery]string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return BadRequest("File path cannot be empty.");
        }

        // The path is canonicalised (relative segments and symbolic links
        // resolved) and checked against the host's allowed roots. The previous
        // check rejected ".." but explicitly permitted absolute paths, so any
        // file on the machine was readable.
        if (!_pathGuard.TryResolve(filePath, out var resolvedFilePath))
        {
            return Forbid();
        }

        filePath = resolvedFilePath;

        if (!System.IO.File.Exists(filePath))
        {
            return NotFound($"Video file '{filePath}' not found.");
        }

        // Determine content type based on extension (important for browser to understand)
        var contentType = MediaTypeNames.Application.Octet; // Default
        string fileExtension = Path.GetExtension(filePath).ToLowerInvariant();

        switch (fileExtension) // TODO LA - Move to Extension
        {
            case ".3gp":
                contentType = "video/3gp2";
                break;
            case ".avi":
                contentType = "video/x-msvideo";
                break;
            //case ".mkv":
            //    contentType = "video/x-matroska";
            //    break;
            case ".mpg":
            case ".mpeg":
                contentType = "video/mpeg";
                break;
            case ".mp4":
            case ".m4v":
            case ".m4p":
                contentType = "video/mp4";
                break;
            case ".ogv":
            case ".ogg":
                contentType = "video/ogg";
                break;
            case ".mov":
                contentType = "video/quicktime";
                break;
            case ".mkv":
            case ".webm":
                contentType = "video/webm";
                break;
            // Add more video types as needed
            default:
                return BadRequest("Unsupported video format.");
        }

        // Return the file stream
        // This is efficient as it streams the file directly from disk
        return File(System.IO.File.OpenRead(filePath), contentType, enableRangeProcessing: true);
    }
}
