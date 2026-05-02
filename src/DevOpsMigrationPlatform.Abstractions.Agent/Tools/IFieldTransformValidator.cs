// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Tools;

/// <summary>
/// Validates all configured transform rules, optionally sampling real work item data
/// to surface type-mismatch or missing-field issues before the migration runs.
/// </summary>
public interface IFieldTransformValidator
{
    /// <summary>Runs validation and returns a report of all findings.</summary>
    /// <param name="sampleSize">Number of work items to sample from the source for live validation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<FieldTransformValidationReport> ValidateAsync(int sampleSize = 10, CancellationToken cancellationToken = default);
}
