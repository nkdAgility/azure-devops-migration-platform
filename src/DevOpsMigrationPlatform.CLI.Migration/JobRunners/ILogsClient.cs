// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.CLI.JobRunners;

public interface ILogsClient
{
    Task<IReadOnlyList<ProgressEvent>> GetProgressAsync(Guid jobId, CancellationToken ct);
}
