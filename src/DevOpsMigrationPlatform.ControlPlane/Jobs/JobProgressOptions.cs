// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.ComponentModel.DataAnnotations;

namespace DevOpsMigrationPlatform.ControlPlane.Jobs;

public sealed class JobProgressOptions
{
    public const string SectionName = "JobProgress";

    [Range(1, 100_000)]
    public int Capacity { get; init; } = 1000;
}
