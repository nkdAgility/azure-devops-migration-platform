#if NET7_0_OR_GREATER
using System.Text.Json.Serialization;
#endif
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Utilities;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Base class for source or target system connection in a <see cref="MigrationJob"/>.
/// Concrete subtypes carry only the fields relevant to their connector type.
///
/// <para>Polymorphic deserialization uses the <c>"$type"</c> discriminator on .NET 7+:</para>
/// <list type="bullet">
///   <item><c>"AzureDevOpsServices"</c> → <see cref="AzureDevOpsJobEndpoint"/></item>
///   <item><c>"TeamFoundationServer"</c> → <see cref="TfsJobEndpoint"/></item>
///   <item><c>"Simulated"</c> → <see cref="SimulatedJobEndpoint"/></item>
/// </list>
/// </summary>
#if NET7_0_OR_GREATER
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(AzureDevOpsJobEndpoint), "AzureDevOpsServices")]
[JsonDerivedType(typeof(TfsJobEndpoint), "TeamFoundationServer")]
[JsonDerivedType(typeof(SimulatedJobEndpoint), "Simulated")]
#endif
public abstract class JobEndpoint
{
    /// <summary>Connector discriminator: AzureDevOpsServices, TeamFoundationServer, or Simulated.</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>The effective URL after <c>$ENV:VARNAME</c> expansion. Empty when not applicable.</summary>
    public virtual string ResolvedUrl => string.Empty;

    /// <summary>Team project name. Empty when not applicable.</summary>
    public virtual string Project { get; init; } = string.Empty;

    /// <summary>API version string. Null when not applicable.</summary>
    public virtual string? ApiVersion { get; init; }

    /// <summary>Authentication credentials. Null when not applicable.</summary>
    public virtual EndpointAuthenticationOptions? Authentication { get; init; }
}
