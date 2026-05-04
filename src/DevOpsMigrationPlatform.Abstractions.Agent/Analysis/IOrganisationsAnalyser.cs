// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Analysis;

public interface IOrganisationsAnalyser : IAnalyser
{
    Task AnalyseAsync(OrganisationsAnalyseContext context, CancellationToken ct);
}

