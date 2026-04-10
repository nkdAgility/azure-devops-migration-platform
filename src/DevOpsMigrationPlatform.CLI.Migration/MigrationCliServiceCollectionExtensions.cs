using DevOpsMigrationPlatform.Infrastructure.AzureDevOps;
using Microsoft.Extensions.DependencyInjection;

namespace DevOpsMigrationPlatform.CLI.Migration;

/// <summary>
/// Composition-root extensions for the migration CLI.
/// This is the ONLY file in <c>DevOpsMigrationPlatform.CLI.Migration</c> that is
/// permitted to reference <c>DevOpsMigrationPlatform.Infrastructure.AzureDevOps</c>.
/// Individual command classes must call these methods rather than calling
/// infrastructure extension methods directly.
/// </summary>
public static class MigrationCliServiceCollectionExtensions
{
    /// <summary>
    /// Registers the services required for pre-flight work item counting before
    /// submitting an export job.  Delegates to the ADO implementation without
    /// leaking that implementation detail into command classes.
    /// </summary>
    public static IServiceCollection AddExportPreflightServices(
        this IServiceCollection services)
        => services.AddAzureDevOpsWorkItemCount();
}
