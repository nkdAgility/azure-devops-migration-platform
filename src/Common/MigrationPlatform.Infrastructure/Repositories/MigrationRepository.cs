using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MigrationPlatform.Abstractions.Models;
using MigrationPlatform.Abstractions.Options;
using MigrationPlatform.Abstractions.Repositories;
using Newtonsoft.Json;
using System.Globalization;

namespace MigrationPlatform.Infrastructure.Repositories
{
    public class MigrationRepository : IMigrationRepository
    {
        private readonly MigrationRepositoryOptions _options;
        private string _workItemRootPath;
        private readonly IWorkItemWatermarkStore _workItemWatermarkStore;
        private readonly ILogger<MigrationRepository> _logger;

        public MigrationRepository(IOptions<MigrationRepositoryOptions> options, ILogger<MigrationRepository> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.Value.RepositoryPath == null) throw new ArgumentException("RepositoryPath cannot be null.", nameof(options));
            _options = options.Value;
            _workItemRootPath = Path.Combine(_options.RepositoryPath, "WorkItems");
            if (!Directory.Exists(_workItemRootPath))
            {
                Directory.CreateDirectory(_workItemRootPath);
            }
            _workItemWatermarkStore = new WorkItemWatermarkStore(Path.Combine(_workItemRootPath, "WorkItemWatermarkStore.sqlite"));
            _workItemWatermarkStore.Initialise();
        }

        public int? GetQueryCount(string query)
        {
            return null;// _workItemWatermarkStore.GetQueryCount(query);
        }

        public void UpdateQueryCount(string query, int count)
        {
            _workItemWatermarkStore.UpdateQueryCount(query, count);
        }

        public int GetWatermark(int workItemId)
        {
            return _workItemWatermarkStore.GetWatermark(workItemId) ?? 0;
        }

        public Boolean IsRevisionProcessed(int workItemId, int revisionIndex)
        {
            return _workItemWatermarkStore.IsRevisionProcessed(workItemId, revisionIndex);
        }

        public void AddWorkItemRevision(MigrationWorkItemRevision revision)
        {

            string folderPath = GetRevisionSavePath(revision);

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }
            string filePath = Path.Combine(folderPath, "revision.json");
            var json = JsonConvert.SerializeObject(revision, Formatting.Indented);
            File.WriteAllText(filePath, json);
            _workItemWatermarkStore.UpdateWatermark(revision.workItemId, revision.Index);
        }

        public void AddWorkItemRevisionAttachment(MigrationWorkItemRevision revision, string fileName, string fileLocation)
        {
            try
            {
                string folderPath = GetRevisionSavePath(revision);

                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }

                string filePath = Path.Combine(folderPath, fileName);

                if (!File.Exists(fileLocation))
                {
                    _logger.LogError("Source attachment file does not exist: {FileLocation} for work item {WorkItemId}", fileLocation, revision.workItemId);
                    return;
                }

                File.Copy(fileLocation, filePath, true);

                _logger.LogInformation("Successfully copied attachment {FileName} for work item {WorkItemId} to {DestinationPath}", fileName, revision.workItemId, filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to copy attachment {FileName} for work item {WorkItemId}", fileName, revision.workItemId);
                // Optionally: Record metrics here too
            }
        }


        public string GetRevisionSavePath(MigrationWorkItemRevision revision)
        {
            var timestamp = revision.ChangedDate.UtcDateTime;
            //string folderPath = Path.Combine(
            //    _workItemRootPath,
            //    timestamp.ToString("yyyy", CultureInfo.InvariantCulture),
            //    timestamp.ToString("MM", CultureInfo.InvariantCulture),
            //    timestamp.ToString("dd", CultureInfo.InvariantCulture),
            //    timestamp.ToString("HH", CultureInfo.InvariantCulture),
            //    timestamp.ToString("mm", CultureInfo.InvariantCulture),
            //    $"{timestamp.Ticks}-{revision.workItemId}-{revision.Index}"
            //);
            string folderPath = Path.Combine(
               _workItemRootPath,
               timestamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
               $"{timestamp.Ticks}-{revision.workItemId}-{revision.Index}"
           );
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }
            return folderPath;
        }
    }
}
