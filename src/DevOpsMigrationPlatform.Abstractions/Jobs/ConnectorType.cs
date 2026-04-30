namespace DevOpsMigrationPlatform.Abstractions.Jobs;

/// <summary>
/// The external system(s) this job connects to.
/// Used by the ControlPlane to match the job to an agent that advertises
/// the required connector capabilities.
/// </summary>
public enum ConnectorType
{
    /// <summary>Azure DevOps Services (cloud).</summary>
    AzureDevOps,

    /// <summary>Team Foundation Server (on-premises). Requires the net481 TfsMigrationAgent.</summary>
    TeamFoundationServer,

    /// <summary>Simulated in-memory source/target for testing and development.</summary>
    Simulated
}
