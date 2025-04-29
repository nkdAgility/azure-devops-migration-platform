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
        public static readonly string ConnectionString = "InstrumentationKey=4e1e7fba-d42a-411a-a413-7bd2ef79f59d;IngestionEndpoint=https://centralus-2.in.applicationinsights.azure.com/;LiveEndpoint=https://centralus.livediagnostics.monitor.azure.com/;ApplicationId=53a54d4e-2813-4c82-8951-f0139a2cef43";
    }
}
