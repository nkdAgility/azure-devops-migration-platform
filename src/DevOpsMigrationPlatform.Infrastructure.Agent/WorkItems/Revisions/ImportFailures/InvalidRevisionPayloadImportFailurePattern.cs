// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Revisions.ImportFailures;

internal sealed class InvalidRevisionPayloadImportFailurePattern : IImportFailurePattern
{
    public const string Code = "WORKITEMS_PREPARE_INVALID_REVISION_PAYLOAD";

    public string PatternCode => Code;

    public async Task<IReadOnlyList<ImportFailureFinding>> EvaluateAsync(
        ImportFailurePatternContext context,
        CancellationToken cancellationToken)
    {
        var findings = new List<ImportFailureFinding>();

        await foreach (var parsedRevision in WorkItemsPrepareRevisionReader.EnumerateAsync(
                           context.PrepareContext.Package,
                           context.Organisation,
                           context.Project,
                           cancellationToken).ConfigureAwait(false))
        {
            if (string.IsNullOrWhiteSpace(parsedRevision.ParseError))
            {
                continue;
            }

            findings.Add(new ImportFailureFinding(
                PatternCode,
                ImportFailureSeverity.Blocking,
                parsedRevision.RevisionJsonPath,
                parsedRevision.ParseError!,
                "Repair the revision.json payload and re-run Prepare."));
        }

        return findings;
    }
}

