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
/// Accepts either <see cref="SimulatedEndpointOptions"/> (direct config) or
/// <see cref="JobEndpointMigrationOptions"/> (agent bridge), extracting the
/// generator config from <see cref="JobEndpoint.Properties"/> in the latter case.
/// </summary>
public sealed class SimulatedWorkItemRevisionSourceFactory : IWorkItemRevisionSourceFactory
{
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

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static SimulatedGeneratorConfig ExtractGeneratorConfig(JobEndpoint jobEndpoint)
    {
        if (jobEndpoint.Properties?.TryGetValue("Generator", out var genObj) == true && genObj is not null)
        {
            if (genObj is JsonElement el)
            {
                return el.Deserialize<SimulatedGeneratorConfig>(_jsonOptions) ?? new SimulatedGeneratorConfig();
            }

            if (genObj is SimulatedGeneratorConfig cfg)
            {
                return cfg;
            }
        }

        return new SimulatedGeneratorConfig();
    }
}
