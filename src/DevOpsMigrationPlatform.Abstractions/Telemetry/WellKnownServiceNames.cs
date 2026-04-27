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

    /// <summary>TFS Migration Agent worker service (.NET Framework).</summary>
    public const string TfsMigrationAgent = "TfsMigrationAgent";
}

/// <summary>
/// OTel resource attribute key constants for deployment context.
/// These appear as custom dimensions in Application Insights and enable
/// filtering by deployment mode and control plane URL.
/// </summary>
public static class WellKnownResourceAttributes
{
    /// <summary>
    /// Resource attribute key for the deployment mode (<c>Standalone</c> or <c>Hosted</c>).
    /// Allows Application Insights queries to distinguish local-only runs from
    /// remote/cloud deployments.
    /// </summary>
    public const string DeploymentMode = "deployment.mode";

    /// <summary>
    /// Resource attribute key for the control plane base URL
    /// (e.g. <c>http://localhost:5100</c> or <c>https://cp.example.com</c>).
    /// Enables correlation of CLI/Agent telemetry with a specific control plane instance.
    /// </summary>
    public const string ControlPlaneUrl = "deployment.controlplane.url";
}
