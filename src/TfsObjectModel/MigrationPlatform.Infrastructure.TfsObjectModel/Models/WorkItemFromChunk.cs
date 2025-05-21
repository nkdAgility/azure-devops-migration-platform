using Microsoft.TeamFoundation.WorkItemTracking.Client;
using MigrationPlatform.Abstractions.Models;

namespace MigrationPlatform.Infrastructure.TfsObjectModel.Models
{
    public class WorkItemFromChunk : WorkItemQueryChunk
    {
        public WorkItem WorkItem { get; set; } = default!;
    }

}
