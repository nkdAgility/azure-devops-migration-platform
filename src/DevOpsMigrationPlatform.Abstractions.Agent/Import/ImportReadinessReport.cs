// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Linq;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Import;

/// <summary>
/// Aggregated WorkItems prepare findings used to determine import readiness.
/// </summary>
public sealed record ImportReadinessReport
{
    public required WorkItemsPrepareReadinessResult Readiness { get; init; }

    public required IReadOnlyList<ImportFailureFinding> Findings { get; init; }

    public required IReadOnlyList<ImportFailureFinding> BlockingFindings { get; init; }

    public required IReadOnlyList<ImportFailureFinding> WarningFindings { get; init; }

    public required IReadOnlyList<ArtefactFinding> ArtefactFindings { get; init; }

    public required IReadOnlyList<FieldTransformFinding> FieldTransformFindings { get; init; }

    public int BlockingCount => BlockingFindings.Count;

    public int WarningCount => WarningFindings.Count;

    public static ImportReadinessReport Create(
        WorkItemsPrepareReadinessResult readiness,
        IReadOnlyList<ImportFailureFinding> findings,
        IReadOnlyList<ArtefactFinding> artefactFindings,
        IReadOnlyList<FieldTransformFinding> fieldTransformFindings)
    {
        var blockingFindings = findings
            .Where(f => f.Severity == ImportFailureSeverity.Blocking)
            .ToList();
        var warningFindings = findings
            .Where(f => f.Severity == ImportFailureSeverity.Warning)
            .ToList();

        return new ImportReadinessReport
        {
            Readiness = readiness,
            Findings = findings,
            BlockingFindings = blockingFindings,
            WarningFindings = warningFindings,
            ArtefactFindings = artefactFindings,
            FieldTransformFindings = fieldTransformFindings
        };
    }
}

