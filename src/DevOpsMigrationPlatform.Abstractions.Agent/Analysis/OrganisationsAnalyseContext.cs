// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Organisations;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Analysis;

public sealed record OrganisationsAnalyseContext : AnalyseContext
{
    public IReadOnlyList<OrganisationEndpoint> Organisations { get; init; } = [];
}

