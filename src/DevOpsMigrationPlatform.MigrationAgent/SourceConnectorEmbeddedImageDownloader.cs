// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Attachments;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Infrastructure.Agent.Export;
using DevOpsMigrationPlatform.Infrastructure.Simulated.Attachments;

namespace DevOpsMigrationPlatform.MigrationAgent;

/// <summary>
/// The MigrationAgent serves Azure DevOps and Simulated sources (#169, #154). Picks the embedded-image
/// downloader for the active job's source connector at download time, because the source endpoint
/// is a live view of the current job. Composition-root selection only (ADR-0022).
/// </summary>
internal sealed class SourceConnectorEmbeddedImageDownloader : IEmbeddedImageDownloader
{
    private readonly ISourceEndpointInfo _source;
    private readonly SourceEndpointEmbeddedImageDownloader _endpointDownloader;
    private readonly SimulatedEmbeddedImageDownloader _simulatedDownloader = new();

    public SourceConnectorEmbeddedImageDownloader(ISourceEndpointInfo source, SourceEndpointEmbeddedImageDownloader endpointDownloader)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _endpointDownloader = endpointDownloader ?? throw new ArgumentNullException(nameof(endpointDownloader));
    }

    public Task<EmbeddedImageDownloadResult?> TryDownloadAsync(string imageUrl, CancellationToken cancellationToken) =>
        string.Equals(_source.ConnectorType, "Simulated", StringComparison.OrdinalIgnoreCase)
            ? _simulatedDownloader.TryDownloadAsync(imageUrl, cancellationToken)
            : _endpointDownloader.TryDownloadAsync(imageUrl, cancellationToken);
}
