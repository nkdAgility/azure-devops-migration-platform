// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.Teams;

/// <summary>Core team metadata exported from the source system.</summary>
public sealed record TeamDefinition(
    string Id,
    string Name,
    string Description,
    bool IsDefault);
