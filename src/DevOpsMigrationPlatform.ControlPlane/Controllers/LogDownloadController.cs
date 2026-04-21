using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.ControlPlane.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.ControlPlane.Controllers;

/// <summary>
/// Downloads package log files from a completed job's package via
/// <see cref="IPackageStoreFactory"/> and <see cref="IArtefactStore"/>.
/// Logs are stored in job-scoped folders: <c>Logs/&lt;ticks&gt;-&lt;jobId&gt;/</c>.
/// Falls back to the flat <c>Logs/</c> folder for packages created before job-scoped logging.
/// <c>GET /jobs/{jobId}/logs/download?type=progress|diagnostics</c>
/// </summary>
[ApiController]
public sealed class LogDownloadController : ControllerBase
{
    private readonly IJobStore _jobStore;
    private readonly IPackageStoreFactory _packageStoreFactory;
    private readonly ILogger<LogDownloadController> _logger;

    public LogDownloadController(
        IJobStore jobStore,
        IPackageStoreFactory packageStoreFactory,
        ILogger<LogDownloadController> logger)
    {
        _jobStore = jobStore;
        _packageStoreFactory = packageStoreFactory;
        _logger = logger;
    }

    /// <summary>
    /// Downloads the specified log file from the job's package.
    /// <c>GET /jobs/{jobId}/logs/download?type=progress|diagnostics</c>
    /// </summary>
    [HttpGet("/jobs/{jobId}/logs/download")]
    [ProducesResponseType(200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DownloadLog(
        Guid jobId,
        [FromQuery] string type,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(type) ||
            (!string.Equals(type, "progress", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(type, "diagnostics", StringComparison.OrdinalIgnoreCase)))
        {
            return BadRequest("Query parameter 'type' must be 'progress' or 'diagnostics'.");
        }

        var job = _jobStore.Get(jobId);
        if (job is null)
            return NotFound($"Job '{jobId}' not found.");

        var packageUri = job switch
        {
            MigrationJob mj => mj.Package.PackageUri,
            DiscoveryJob dj => dj.Package.PackageUri,
            _ => null
        };
        if (string.IsNullOrWhiteSpace(packageUri))
            return NotFound("Job has no package URI.");

        var fileName = string.Equals(type, "diagnostics", StringComparison.OrdinalIgnoreCase)
            ? "agent.jsonl"
            : "progress.jsonl";

        try
        {
            var (store, _) = _packageStoreFactory.Create(packageUri);

            var logPath = await ResolveLogPathAsync(store, jobId.ToString(), fileName, ct);

            var exists = await store.ExistsAsync(logPath, ct);
            if (!exists)
                return NotFound($"Log file '{logPath}' not found in the package.");

            var content = await store.ReadAsync(logPath, ct);
            if (content is null)
                return NotFound($"Log file '{logPath}' not found in the package.");

            return Content(content, "application/x-ndjson");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read {FileName} from package {PackageUri} for job {JobId}.",
                fileName, packageUri, jobId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                "Failed to read log file from the package.");
        }
    }

    /// <summary>
    /// Resolves the full artefact path for a log file by finding the job-scoped subfolder
    /// under <c>Logs/</c> whose name ends with <c>-{jobId}</c>.
    /// Falls back to the flat <c>Logs/{fileName}</c> for backward compatibility.
    /// </summary>
    private static async Task<string> ResolveLogPathAsync(
        IArtefactStore store, string jobId, string fileName, CancellationToken ct)
    {
        // Look for job-scoped folder: Logs/<ticks>-<jobId>/
        await foreach (var entry in store.EnumerateAsync("Logs/", ct))
        {
            // EnumerateAsync returns relative paths like "Logs/<ticks>-<jobId>/agent.jsonl"
            // We look for a subfolder whose name ends with the jobId
            var segments = entry.Split('/');
            if (segments.Length >= 2 && segments[1].EndsWith($"-{jobId}", StringComparison.OrdinalIgnoreCase))
            {
                return $"Logs/{segments[1]}/{fileName}";
            }
        }

        // Backward compatibility: flat Logs/ folder
        return $"Logs/{fileName}";
    }
}
