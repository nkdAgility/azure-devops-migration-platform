using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.DependencyInjection;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Connectors;

/// <summary>
/// Dispatches all <see cref="ITeamSource"/> calls to the concrete implementation
/// registered for the endpoint's <c>Type</c> discriminator.
/// </summary>
public sealed class CompositeTeamSource : ITeamSource
{
    private readonly IReadOnlyDictionary<string, Type> _sourceTypes;
    private readonly IServiceProvider _serviceProvider;

    public CompositeTeamSource(
        IEnumerable<KeyedTeamSource> registrations,
        IServiceProvider serviceProvider)
    {
        var dict = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        foreach (var reg in registrations)
            dict[reg.Key] = reg.SourceType;
        _sourceTypes = dict;
        _serviceProvider = serviceProvider;
    }

    private ITeamSource Resolve(MigrationEndpointOptions endpoint)
    {
        if (endpoint is null) throw new ArgumentNullException(nameof(endpoint));

        var typeKey = endpoint.Type;
        if (string.IsNullOrWhiteSpace(typeKey))
            throw new ArgumentException("Endpoint has no Type discriminator.", nameof(endpoint));

        if (!_sourceTypes.TryGetValue(typeKey, out var sourceType))
            throw new InvalidOperationException(
                $"No ITeamSource is registered for endpoint type '{typeKey}'. " +
                "Register one with AddTeamSource(key, implementation).");

        return (ITeamSource)_serviceProvider.GetRequiredService(sourceType);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<TeamDefinition> EnumerateTeamsAsync(
        MigrationEndpointOptions endpoint, string projectName,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var team in Resolve(endpoint).EnumerateTeamsAsync(endpoint, projectName, ct))
            yield return team;
    }

    /// <inheritdoc/>
    public Task<TeamSettings?> GetTeamSettingsAsync(
        MigrationEndpointOptions endpoint, string projectName, string teamId, CancellationToken ct)
        => Resolve(endpoint).GetTeamSettingsAsync(endpoint, projectName, teamId, ct);

    /// <inheritdoc/>
    public async IAsyncEnumerable<TeamIteration> GetTeamIterationsAsync(
        MigrationEndpointOptions endpoint, string projectName, string teamId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var iteration in Resolve(endpoint).GetTeamIterationsAsync(endpoint, projectName, teamId, ct))
            yield return iteration;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<TeamMember> GetTeamMembersAsync(
        MigrationEndpointOptions endpoint, string projectName, string teamId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var member in Resolve(endpoint).GetTeamMembersAsync(endpoint, projectName, teamId, ct))
            yield return member;
    }

    /// <inheritdoc/>
    public Task<TeamCapacityEntry[]> GetTeamCapacityAsync(
        MigrationEndpointOptions endpoint, string projectName, string teamId, string iterationId, CancellationToken ct)
        => Resolve(endpoint).GetTeamCapacityAsync(endpoint, projectName, teamId, iterationId, ct);

    /// <inheritdoc/>
    public Task<TeamAreaPaths?> GetTeamAreaPathsAsync(
        MigrationEndpointOptions endpoint, string projectName, string teamId, CancellationToken ct)
        => Resolve(endpoint).GetTeamAreaPathsAsync(endpoint, projectName, teamId, ct);
}

/// <summary>Registration descriptor for a keyed <see cref="ITeamSource"/>.</summary>
public sealed record KeyedTeamSource(string Key, Type SourceType);
