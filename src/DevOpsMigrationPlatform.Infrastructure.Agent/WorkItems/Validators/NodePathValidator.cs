// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.FailurePatterns;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Validators;

internal sealed class NodePathValidator(INodeCreator nodeCreator) : IImportFailurePattern
{
    public const string Code = "WORKITEMS_PREPARE_MISSING_NODE_PATH";

    public string PatternCode => Code;

    public async Task<IReadOnlyList<ImportFailureFinding>> EvaluateAsync(
        ImportFailurePatternContext context,
        CancellationToken cancellationToken)
    {
        var areaPaths = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        var iterationPaths = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

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
                if (field.Value is not string value || string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                var trimmedValue = value.Trim();
                if (string.Equals(field.ReferenceName, "System.AreaPath", System.StringComparison.OrdinalIgnoreCase))
                {
                    areaPaths.Add(trimmedValue);
                }
                else if (string.Equals(field.ReferenceName, "System.IterationPath", System.StringComparison.OrdinalIgnoreCase))
                {
                    iterationPaths.Add(trimmedValue);
                }
            }
        }

        var findings = new List<ImportFailureFinding>();
        findings.AddRange(await EvaluateNodeTypeAsync(
            ClassificationNodeType.Area,
            "System.AreaPath",
            areaPaths,
            cancellationToken).ConfigureAwait(false));
        findings.AddRange(await EvaluateNodeTypeAsync(
            ClassificationNodeType.Iteration,
            "System.IterationPath",
            iterationPaths,
            cancellationToken).ConfigureAwait(false));

        return findings;
    }

    private async Task<IReadOnlyList<ImportFailureFinding>> EvaluateNodeTypeAsync(
        ClassificationNodeType nodeType,
        string fieldName,
        IEnumerable<string> paths,
        CancellationToken cancellationToken)
    {
        var findings = new List<ImportFailureFinding>();
        foreach (var path in paths.OrderBy(p => p, System.StringComparer.OrdinalIgnoreCase))
        {
            var exists = await nodeCreator.NodeExistsAsync(nodeType, path, cancellationToken).ConfigureAwait(false);
            if (exists)
            {
                continue;
            }

            findings.Add(new ImportFailureFinding(
                PatternCode,
                ImportFailureSeverity.Blocking,
                $"{fieldName}|{path}",
                $"Required {fieldName} path '{path}' does not exist on the target project.",
                $"Create or map '{path}' on the target before running import."));
        }

        return findings;
    }
}
