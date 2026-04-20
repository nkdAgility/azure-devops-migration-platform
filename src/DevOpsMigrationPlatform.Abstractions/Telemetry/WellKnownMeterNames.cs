using System;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Meter name constants shared across the solution.
/// Defined in Abstractions so .NET 10 hosts can register meters without referencing
/// the .NET 4.8 Infrastructure.TfsObjectModel assembly (Principle VI).
/// </summary>
public static class WellKnownMeterNames
{
    /// <summary>Consolidated meter for all migration work item metrics (v2.0).</summary>
    public const string Migration = "DevOpsMigrationPlatform.Migration";

    /// <summary>Consolidated meter for all discovery metrics (inventory + dependencies).</summary>
    public const string Discovery = "DevOpsMigrationPlatform.Discovery";

    [Obsolete("Use Migration. Will be removed in next major version.")]
    public const string WorkItemExport = "DevOpsMigrationPlatform.WorkItemExport";

    [Obsolete("Use Migration. Will be removed in next major version.")]
    public const string AttachmentDownload = "DevOpsMigrationPlatform.AttachmentDownload";
}
