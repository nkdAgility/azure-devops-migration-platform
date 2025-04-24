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

        public MigrationRepository(IOptions<MigrationRepositoryOptions> options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.Value.RepositoryPath == null) throw new ArgumentException("RepositoryPath cannot be null.", nameof(options));
            _options = options.Value;
            ConfigureWorkItemRepository(_options);
        }

        public void ConfigureWorkItemRepository(MigrationRepositoryOptions options)
        {
            if (options.RepositoryPath == null) throw new ArgumentException("RepositoryPath cannot be null.", nameof(options));

            _workItemRootPath = Path.Combine(options.RepositoryPath, "WorkItems");
            if (!Directory.Exists(_workItemRootPath))
            {
                Directory.CreateDirectory(_workItemRootPath);
            }
        }

        public void AddWorkItemRevision(MigrationWorkItemRevision revision)
        {
            var timestamp = revision.ChangedDate.UtcDateTime;
            string folderPath = Path.Combine(
                _workItemRootPath,
                timestamp.ToString("yyyy", CultureInfo.InvariantCulture),
                timestamp.ToString("MM", CultureInfo.InvariantCulture),
                timestamp.ToString("dd", CultureInfo.InvariantCulture),
                timestamp.ToString("HH", CultureInfo.InvariantCulture),
                timestamp.ToString("mm", CultureInfo.InvariantCulture),
                timestamp.ToString("ss", CultureInfo.InvariantCulture),
                timestamp.ToString("fff", CultureInfo.InvariantCulture),
                $"{revision.id}-{revision.Index}"
            );
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }
            string filePath = Path.Combine(folderPath, "revision.json");
            var json = JsonConvert.SerializeObject(revision, Formatting.Indented);
            File.WriteAllText(filePath, json);

        }
    }
}
