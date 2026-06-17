// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;

namespace DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

public interface IImportCreatedNodeStateStore
{
    Task<IReadOnlyCollection<string>> GetRecordedCreatedNodeKeysAsync(CancellationToken cancellationToken);

    Task RecordCreatedNodePathAsync(
        ClassificationNodeType nodeType,
        string nodePath,
        CancellationToken cancellationToken);
}
