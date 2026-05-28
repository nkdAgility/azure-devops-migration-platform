// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Export;

public interface IWorkItemExportOrchestrator
{
    Task ExportAsync(IWorkItemRevisionSource source, CancellationToken cancellationToken);
}
