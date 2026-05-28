// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Revisions.ImportFailures;

internal sealed class MissingRevisionArtefactImportFailurePattern : IImportFailurePattern
{
    public const string Code = "WORKITEMS_PREPARE_MISSING_REVISION_ARTEFACT";

    public string PatternCode => Code;

    public async Task<IReadOnlyList<ImportFailureFinding>> EvaluateAsync(
        ImportFailurePatternContext context,
        CancellationToken cancellationToken)
    {
        var hasRevision = false;
        await foreach (var _ in WorkItemsPrepareRevisionReader.EnumerateAsync(context.PrepareContext.Package, cancellationToken).ConfigureAwait(false))
        {
            hasRevision = true;
            break;
        }

        if (hasRevision)
        {
            return [];
        }

        return
        [
            new ImportFailureFinding(
                PatternCode,
                ImportFailureSeverity.Blocking,
                "WorkItems/",
                "No revision.json artefacts were found under WorkItems/.",
                "Re-run export and verify WorkItems revision folders were produced before re-running Prepare.")
        ];
    }

}

