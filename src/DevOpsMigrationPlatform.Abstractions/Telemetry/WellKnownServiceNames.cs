namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// OTel <c>service.name</c> and <c>service.namespace</c> constants used to identify
/// each component on the Application Insights Application Map.
/// Defined in Abstractions so every host resolves the same values.
/// </summary>
public static class WellKnownServiceNames
{
    /// <summary>Shared <c>service.namespace</c> for all platform components.</summary>
    public const string Namespace = "DevOpsMigrationPlatform";

    /// <summary>Migration Agent worker service.</summary>
    public const string MigrationAgent = "MigrationAgent";

    /// <summary>Control Plane API host.</summary>
    public const string ControlPlaneHost = "ControlPlaneHost";

    /// <summary>CLI migration tool (console host).</summary>
    public const string Cli = "CLI";

    /// <summary>TFS Object Model export subprocess (.NET Framework).</summary>
    public const string TfsExport = "TfsExport";
}
