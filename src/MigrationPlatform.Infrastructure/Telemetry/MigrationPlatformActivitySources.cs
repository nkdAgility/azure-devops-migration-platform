using System.Diagnostics;

namespace MigrationPlatform.Infrastructure.Telemetry
{
    public static class MigrationPlatformActivitySources
    {
        public static readonly ActivitySource WorkItemExport = new("MigrationPlatform.WorkItemExport");
        public static readonly ActivitySource WorkItemImport = new("MigrationPlatform.WorkItemImport");
        public static readonly ActivitySource AttachmentDownload = new("MigrationPlatform.AttachmentDownload");
        public static readonly ActivitySource GitMigration = new("MigrationPlatform.GitMigration");
        // Add more if needed...
    }
}
