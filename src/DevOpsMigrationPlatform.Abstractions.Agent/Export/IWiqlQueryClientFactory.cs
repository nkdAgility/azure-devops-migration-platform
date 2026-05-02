// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Organisations;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Export;

/// <summary>
/// Creates an <see cref="IWiqlQueryClient"/> for a given <see cref="OrganisationEndpoint"/>,
/// keeping connection concerns separate from the windowing strategy logic.
/// </summary>
public interface IWiqlQueryClientFactory
{
    Task<IWiqlQueryClient> CreateAsync(
        OrganisationEndpoint endpoint,
        CancellationToken cancellationToken = default);
}
