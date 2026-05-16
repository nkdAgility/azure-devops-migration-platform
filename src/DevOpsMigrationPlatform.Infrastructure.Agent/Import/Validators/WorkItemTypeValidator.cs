// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Infrastructure.Agent.Import.FailurePatterns;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Import.Validators;

internal sealed class WorkItemTypeValidator(IWorkItemTypeReadinessTargetFactory typeReadinessTargetFactory) : IImportFailurePattern
{
    public const string Code = "WORKITEMS_PREPARE_MISSING_WORKITEM_TYPE";
    private const string WorkItemTypeReferenceName = "System.WorkItemType";

    public string PatternCode => Code;

    public async Task<IReadOnlyList<ImportFailureFinding>> EvaluateAsync(
        ImportFailurePatternContext context,
        CancellationToken cancellationToken)
    {
        var exportedTypes = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        await foreach (var parsedRevision in WorkItemsPrepareRevisionReader.EnumerateAsync(
                           context.PrepareContext.Package,
                           cancellationToken).ConfigureAwait(false))
        {
            if (parsedRevision.Revision is null)
            {
                continue;
            }

            foreach (var field in parsedRevision.Revision.Fields)
            {
                if (!string.Equals(field.ReferenceName, WorkItemTypeReferenceName, System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (field.Value is not string value || string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                exportedTypes.Add(value.Trim());
            }
        }

        if (exportedTypes.Count == 0)
        {
            return [];
        }

        var target = await typeReadinessTargetFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
        var findings = new List<ImportFailureFinding>();
        foreach (var workItemType in exportedTypes.OrderBy(t => t, System.StringComparer.OrdinalIgnoreCase))
        {
            var exists = await target.WorkItemTypeExistsAsync(workItemType, cancellationToken).ConfigureAwait(false);
            if (exists)
            {
                continue;
            }

            findings.Add(new ImportFailureFinding(
                PatternCode,
                ImportFailureSeverity.Blocking,
                workItemType,
                $"Required work item type '{workItemType}' does not exist on the target project.",
                $"Create or map '{workItemType}' on the target before running import."));
        }

        return findings;
    }
}
