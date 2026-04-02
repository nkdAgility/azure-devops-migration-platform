namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Meter name constants shared across the solution.
/// Defined in Abstractions so .NET 10 hosts can register meters without referencing
/// the .NET 4.8 Infrastructure.TfsObjectModel assembly (Principle VI).
/// </summary>
public static class WellKnownMeterNames
{
    public const string WorkItemExport     = "DevOpsMigrationPlatform.WorkItemExport";
    public const string AttachmentDownload = "DevOpsMigrationPlatform.AttachmentDownload";
}
