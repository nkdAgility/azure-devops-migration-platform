using MigrationPlatform.Abstractions.Models;

namespace MigrationPlatform.Abstractions.Repositories
{
    public interface IMigrationRepository
    {
        void AddWorkItemRevision(MigrationWorkItemRevision mWorkItem);
        public Boolean IsRevisionProcessed(int workItemId, int revisionIndex);
        public int? GetQueryCount(string query);
        public void UpdateQueryCount(string query, int count);
        int GetWatermark(int id);
        void AddWorkItemRevisionAttachment(MigrationWorkItemRevision revision, string fileName, string fileLocation);
    }
}
