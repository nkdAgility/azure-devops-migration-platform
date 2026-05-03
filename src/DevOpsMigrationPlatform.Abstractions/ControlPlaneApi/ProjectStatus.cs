// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;

/// <summary>
/// Status of a project within a <see cref="JobSnapshot"/>.
/// </summary>
public enum ProjectStatus
{
    /// <summary>Project has not started processing.</summary>
    Pending,

    /// <summary>Project is currently being processed.</summary>
    InProgress,

    /// <summary>Project completed successfully.</summary>
    Completed,

    /// <summary>Project processing failed.</summary>
    Failed
}
