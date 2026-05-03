// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Teams;

/// <summary>Team board/backlog configuration settings.</summary>
public sealed record TeamSettings(
    string BacklogNavigationLevel,
    bool BugsBehavior,
    IReadOnlyList<string> WorkingDays);
