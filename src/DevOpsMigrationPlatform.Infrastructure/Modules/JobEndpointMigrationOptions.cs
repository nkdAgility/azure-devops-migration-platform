#if !NET481
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Modules;

/// <summary>
/// Adapter that exposes a <see cref="JobEndpoint"/> as a <see cref="MigrationEndpointOptions"/>
/// so that <c>WorkItemsModule</c> can pass job source/target data through the
/// <see cref="IWorkItemRevisionSourceFactory"/> and <see cref="IWorkItemImportTargetFactory"/>
/// interfaces without taking a dependency on <c>Infrastructure.AzureDevOps</c>.
///
/// Connector-specific factories (e.g. <c>AzureDevOpsWorkItemRevisionSourceFactory</c>)
/// recognise this type and extract the fields they need.
/// </summary>
public sealed class JobEndpointMigrationOptions : MigrationEndpointOptions
{
    /// <summary>The underlying job endpoint carrying all connection fields.</summary>
    public JobEndpoint JobEndpoint { get; }

    public JobEndpointMigrationOptions(JobEndpoint jobEndpoint)
    {
        JobEndpoint = jobEndpoint ?? throw new System.ArgumentNullException(nameof(jobEndpoint));
        Type = jobEndpoint.Type;
    }
}
#endif
