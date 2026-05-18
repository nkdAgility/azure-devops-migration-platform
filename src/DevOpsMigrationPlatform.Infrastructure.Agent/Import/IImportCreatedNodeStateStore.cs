// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Import;

public interface IImportCreatedNodeStateStore
{
    Task<IReadOnlyCollection<string>> GetRecordedCreatedNodeKeysAsync(CancellationToken cancellationToken);

    Task RecordCreatedNodePathAsync(
        ClassificationNodeType nodeType,
        string nodePath,
        CancellationToken cancellationToken);
}
