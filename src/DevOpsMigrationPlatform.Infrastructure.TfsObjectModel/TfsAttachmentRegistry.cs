// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel;

/// <summary>
/// Registers the TFS integer attachment ID for each (workItemId, revisionIndex, attachmentName)
/// triple so that <see cref="TfsAttachmentBinarySource"/> can look up the download ID after
/// <see cref="TfsWorkItemRevisionSource"/> yields a revision.
/// </summary>
public sealed class TfsAttachmentRegistry
{
    private readonly Dictionary<(int workItemId, int revisionIndex, string name), int> _map = new();

    /// <summary>Registers a TFS attachment ID for the given triple.</summary>
    public void Register(int workItemId, int revisionIndex, string attachmentName, int tfsAttachmentId) =>
        _map[(workItemId, revisionIndex, attachmentName)] = tfsAttachmentId;

    /// <summary>
    /// Returns <see langword="true"/> and the TFS attachment ID if the triple was previously
    /// registered, otherwise <see langword="false"/>.
    /// </summary>
    public bool TryGet(int workItemId, int revisionIndex, string attachmentName, out int tfsAttachmentId) =>
        _map.TryGetValue((workItemId, revisionIndex, attachmentName), out tfsAttachmentId);
}
