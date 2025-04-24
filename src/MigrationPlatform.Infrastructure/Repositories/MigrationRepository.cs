using Microsoft.Extensions.Options;
using MigrationPlatform.Abstractions.Models;
using MigrationPlatform.Abstractions.Options;
using MigrationPlatform.Abstractions.Repositories;
using Newtonsoft.Json;

namespace MigrationPlatform.Infrastructure.Repositories
{
    public class MigrationRepository : IMigrationRepository
    {
        private readonly MigrationRepositoryOptions _options;
        private string _workItemRootPath;
        private readonly IWorkItemWatermarkStore _workItemWatermarkStore;

        public MigrationRepository(IOptions<MigrationRepositoryOptions> options)
        {
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
            return _workItemWatermarkStore.GetQueryCount(query);
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

        public void AddWorkItemRevisionAttachment(MigrationWorkItemRevision revision, string fileName, string fileLocation, string comment)
        {
            string folderPath = GetRevisionSavePath(revision);
            string filePath = Path.Combine(folderPath, fileName);
            File.Copy(fileLocation, filePath, true);
            revision.Attachments.Add(new MigrationWorkItemAttachment(fileName, comment));
        }

        public string GetRevisionSavePath(MigrationWorkItemRevision revision)
        {
            var timestamp = revision.ChangedDate.UtcDateTime;
            string folderPath = Path.Combine(_workItemRootPath, $"{revision.workItemId}", $"{revision.Index}");
            //string folderPath = Path.Combine(
            //    _workItemRootPath,
            //    timestamp.ToString("yyyy", CultureInfo.InvariantCulture),
            //    timestamp.ToString("MM", CultureInfo.InvariantCulture),
            //    timestamp.ToString("dd", CultureInfo.InvariantCulture),
            //    timestamp.ToString("HH", CultureInfo.InvariantCulture),
            //    timestamp.ToString("mm", CultureInfo.InvariantCulture),
            //    $"{timestamp.Ticks}-{revision.workItemId}-{revision.Index}"
            //);
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }
            return folderPath;
        }
    }
}
