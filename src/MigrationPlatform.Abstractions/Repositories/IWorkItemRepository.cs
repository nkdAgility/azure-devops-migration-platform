using MigrationPlatform.Abstractions.Models;

namespace MigrationPlatform.Abstractions.Repositories
{
    public interface IMigrationRepository
    {
        void AddWorkItemRevision(MigrationWorkItemRevision mWorkItem);
    }
}
