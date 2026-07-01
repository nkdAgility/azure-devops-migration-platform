// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.ComponentModel.DataAnnotations;

namespace DevOpsMigrationPlatform.ControlPlane.Jobs;

public sealed class JobProgressOptions
{
    public const string SectionName = "JobProgress";

    /// <summary>
    /// Maximum events retained per job before further events are discarded with a warning.
    /// The append-only log never silently wraps; this is a hard safety cap.
    /// </summary>
    [Range(1, 1_000_000)]
    public int MaxEventsPerJob { get; init; } = 50_000;
}
