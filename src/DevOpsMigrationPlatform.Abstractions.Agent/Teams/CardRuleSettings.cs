// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Teams;

/// <summary>Card colour-coding rule settings for a Kanban board.</summary>
public sealed record CardRuleSettings(
    IReadOnlyList<CardRule> Rules);
