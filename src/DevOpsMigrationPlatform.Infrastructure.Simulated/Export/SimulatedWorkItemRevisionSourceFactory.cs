using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Modules;
using DevOpsMigrationPlatform.Infrastructure.Simulated.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Export;

/// <summary>
/// Creates a <see cref="SimulatedWorkItemRevisionSource"/> for endpoints with
/// <c>Type == "Simulated"</c>. No credentials are required.
/// Accepts <see cref="SimulatedEndpointOptions"/> (direct config) or
/// <see cref="JobEndpointMigrationOptions"/> wrapping a <see cref="SimulatedJobEndpoint"/>
/// (agent bridge), extracting the generator config from <see cref="SimulatedJobEndpoint.Generator"/>.
/// </summary>
public sealed class SimulatedWorkItemRevisionSourceFactory : IWorkItemRevisionSourceFactory
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <inheritdoc/>
    public Task<IWorkItemRevisionSource> CreateAsync(
        MigrationEndpointOptions endpoint,
        CancellationToken cancellationToken)
    {
        if (endpoint is null)
            throw new ArgumentNullException(nameof(endpoint));

        SimulatedGeneratorConfig generatorConfig;

        if (endpoint is SimulatedEndpointOptions simOpts)
        {
            generatorConfig = simOpts.Generator;
        }
        else if (endpoint is JobEndpointMigrationOptions jobOpts)
        {
            generatorConfig = ExtractGeneratorConfig(jobOpts.JobEndpoint);
        }
        else
        {
            throw new ArgumentException(
                $"Expected {nameof(SimulatedEndpointOptions)} or {nameof(JobEndpointMigrationOptions)} " +
                $"but received {endpoint.GetType().Name}. Ensure the source endpoint type is 'Simulated'.",
                nameof(endpoint));
        }

        return Task.FromResult<IWorkItemRevisionSource>(
            new SimulatedWorkItemRevisionSource(generatorConfig));
    }

    private static SimulatedGeneratorConfig ExtractGeneratorConfig(JobEndpoint jobEndpoint)
    {
        // Prefer the typed SimulatedJobEndpoint.Generator field.
        if (jobEndpoint is SimulatedJobEndpoint simEndpoint && simEndpoint.Generator is not null)
        {
            return DeserializeGenerator(simEndpoint.Generator);
        }

        return new SimulatedGeneratorConfig();
    }

    private static SimulatedGeneratorConfig DeserializeGenerator(object generator)
    {
        if (generator is SimulatedGeneratorConfig cfg)
            return cfg;

        if (generator is JsonElement el)
            return el.Deserialize<SimulatedGeneratorConfig>(_jsonOptions) ?? new SimulatedGeneratorConfig();

        return new SimulatedGeneratorConfig();
    }
}
