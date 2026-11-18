// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Attachments;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.JobLifecycle.TfsExecution;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Attachments;

/// <summary>
/// <see cref="IAttachmentBinarySource"/> adapter for the TFS agent: delegates to the attachment
/// source of the active job. Without it the agent registered none and every TFS export wrote
/// no attachment binaries to the package (#171).
/// </summary>
public sealed class TfsActiveJobAttachmentBinarySource : IAttachmentBinarySource
{
    private readonly ActiveTfsJobServices _activeServices;

    public TfsActiveJobAttachmentBinarySource(ActiveTfsJobServices activeServices)
    {
        _activeServices = activeServices ?? throw new ArgumentNullException(nameof(activeServices));
    }

    /// <inheritdoc/>
    public Task<byte[]?> GetBytesAsync(int workItemId, int revisionIndex, AttachmentMetadata attachment, CancellationToken cancellationToken)
        => _activeServices.Require().AttachmentSource.GetBytesAsync(workItemId, revisionIndex, attachment, cancellationToken);
}
