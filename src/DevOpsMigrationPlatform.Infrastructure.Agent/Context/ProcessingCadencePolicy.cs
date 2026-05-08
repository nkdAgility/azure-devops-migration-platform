// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Context;

/// <summary>
/// Determines when long-running loops should persist durable progress.
/// </summary>
internal sealed class ProcessingCadencePolicy
{
    public bool ShouldPersist(DateTimeOffset nowUtc, DateTimeOffset lastPersistUtc, int processedSincePersist, int minimumBatchSize, TimeSpan maxInterval)
        => processedSincePersist >= minimumBatchSize || nowUtc - lastPersistUtc >= maxInterval;

    public static double ReplayCoverageRatio(int totalProcessed, int replayedAfterResume)
    {
        if (totalProcessed <= 0)
            return 1d;

        var kept = Math.Max(0, totalProcessed - replayedAfterResume);
        return (double)kept / totalProcessed;
    }
}
