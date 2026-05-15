// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Storage;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Import.FailurePatterns;

internal sealed class MissingRevisionArtefactImportFailurePattern : IImportFailurePattern
{
    public const string Code = "WORKITEMS_PREPARE_MISSING_REVISION_ARTEFACT";

    public string PatternCode => Code;

    public async Task<IReadOnlyList<ImportFailureFinding>> EvaluateAsync(
        ImportFailurePatternContext context,
        CancellationToken cancellationToken)
    {
        var hasRevision = false;
        await foreach (var artefactPath in context.PrepareContext.Package.EnumerateContentAsync(
                           new PackageContentContext(PackageContentKind.Collection, Address: new RelativePathAddress("WorkItems/"), IsCollectionRequest: true),
                           cancellationToken).ConfigureAwait(false))
        {
            if (artefactPath.EndsWith("/revision.json", System.StringComparison.Ordinal))
            {
                hasRevision = true;
                break;
            }
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

    private sealed class RelativePathAddress(string relativePath) : IPackageContentAddress
    {
        public string RelativePath => relativePath.Replace('\\', '/').TrimStart('/');
    }
}

