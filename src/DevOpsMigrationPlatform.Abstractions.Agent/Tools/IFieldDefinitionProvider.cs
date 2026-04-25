using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Tools;

/// <summary>
/// Retrieves the canonical set of field definitions from a source or target system.
/// Used by validators and transform implementations to type-check values at runtime.
/// </summary>
public interface IFieldDefinitionProvider
{
    /// <summary>Returns all field definitions visible from the connected endpoint.</summary>
    Task<IReadOnlyList<FieldDefinition>> GetFieldDefinitionsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Creates an <see cref="IFieldDefinitionProvider"/> scoped to a specific endpoint connection.</summary>
public interface IFieldDefinitionProviderFactory
{
    /// <summary>Creates a provider for the named endpoint.</summary>
    IFieldDefinitionProvider Create(string endpointName);
}
