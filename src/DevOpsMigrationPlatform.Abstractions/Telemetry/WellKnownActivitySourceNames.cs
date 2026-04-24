namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Shared <see cref="System.Diagnostics.ActivitySource"/> name constants for distributed tracing.
/// Defined in Abstractions so all hosts can register sources without referencing concrete telemetry.
/// </summary>
public static class WellKnownActivitySourceNames
{
    /// <summary>ActivitySource for export/import/validate operations.</summary>
    public const string Migration = "DevOpsMigrationPlatform.Migration";

    /// <summary>ActivitySource for inventory and dependency discovery operations.</summary>
    public const string Discovery = "DevOpsMigrationPlatform.Discovery";

    /// <summary>ActivitySource for job lifecycle operations in the control plane.</summary>
    public const string ControlPlane = "DevOpsMigrationPlatform.ControlPlane";
}
