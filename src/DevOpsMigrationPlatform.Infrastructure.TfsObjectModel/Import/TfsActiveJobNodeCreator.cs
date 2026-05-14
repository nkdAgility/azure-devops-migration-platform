// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Import;

/// <summary>
/// <see cref="INodeCreator"/> adapter for the TFS agent.
/// Delegates to the node creator from the currently active <see cref="TfsJobServices"/>.
/// </summary>
public sealed class TfsActiveJobNodeCreator : INodeCreator
{
    private readonly ActiveTfsJobServices _activeServices;

    public TfsActiveJobNodeCreator(ActiveTfsJobServices activeServices)
    {
        _activeServices = activeServices ?? throw new ArgumentNullException(nameof(activeServices));
    }

    /// <inheritdoc />
    public Task<bool> NodeExistsAsync(ClassificationNodeType nodeType, string path, CancellationToken ct)
        => _activeServices.Require().NodeCreator.NodeExistsAsync(nodeType, path, ct);

    /// <inheritdoc />
    public Task EnsureExistsAsync(ClassificationNodeType nodeType, string path, CancellationToken ct)
        => _activeServices.Require().NodeCreator.EnsureExistsAsync(nodeType, path, ct);

    /// <inheritdoc />
    public Task SetIterationDatesAsync(
        string path,
        DateTimeOffset? startDate,
        DateTimeOffset? finishDate,
        CancellationToken ct)
        => _activeServices.Require().NodeCreator.SetIterationDatesAsync(path, startDate, finishDate, ct);
}
