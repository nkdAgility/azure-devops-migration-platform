using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.ControlPlane.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.ControlPlane.Controllers;

/// <summary>
/// Downloads package log files (<c>Logs/progress.jsonl</c> or <c>Logs/agent.jsonl</c>)
/// from a completed job's package via <see cref="IPackageStoreFactory"/> and <see cref="IArtefactStore"/>.
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
            MigrationJob mj => mj.Artefacts.PackageUri,
            DiscoveryJob dj => dj.Artefacts.PackageUri,
            _ => null
        };
        if (string.IsNullOrWhiteSpace(packageUri))
            return NotFound("Job has no package URI.");

        var logPath = string.Equals(type, "diagnostics", StringComparison.OrdinalIgnoreCase)
            ? "Logs/agent.jsonl"
            : "Logs/progress.jsonl";

        try
        {
            var (store, _) = _packageStoreFactory.Create(packageUri);

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
            _logger.LogError(ex, "Failed to read {LogPath} from package {PackageUri} for job {JobId}.",
                logPath, packageUri, jobId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                "Failed to read log file from the package.");
        }
    }
}
