namespace MigrationPlatform.Abstractions.Repositories
{
    public interface IWorkItemWatermarkStore
    {
        void Initialise();
        void UpdateWatermark(int workItemId, int revisionIndex);
        int? GetWatermark(int workItemId);
        bool IsRevisionProcessed(int workItemId, int revisionIndex);
        public int? GetQueryCount(string query);
        public void UpdateQueryCount(string query, int count);

    }

}
