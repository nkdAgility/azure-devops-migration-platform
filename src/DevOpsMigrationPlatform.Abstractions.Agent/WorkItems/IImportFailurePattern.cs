// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

/// <summary>
/// Composable prepare-time failure pattern contract for import-capable modules.
/// </summary>
public interface IImportFailurePattern
{
    /// <summary>
    /// Stable machine identifier for this failure pattern implementation.
    /// </summary>
    string PatternCode { get; }

    /// <summary>
    /// Evaluates the failure pattern against the provided prepare context.
    /// </summary>
    Task<IReadOnlyList<ImportFailureFinding>> EvaluateAsync(
        ImportFailurePatternContext context,
        CancellationToken cancellationToken);
}

