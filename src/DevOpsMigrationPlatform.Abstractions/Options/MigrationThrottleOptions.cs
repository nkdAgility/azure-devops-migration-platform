// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>API request concurrency throttle.</summary>
public class MigrationThrottleOptions
{
    /// <summary>Maximum number of in-flight API requests across the platform.  Default: <c>4</c>.</summary>
    public int MaxConcurrency { get; set; } = 4;
}
