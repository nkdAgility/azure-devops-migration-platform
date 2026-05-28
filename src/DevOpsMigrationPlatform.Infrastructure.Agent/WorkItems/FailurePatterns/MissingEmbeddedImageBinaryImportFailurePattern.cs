// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Storage;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.FailurePatterns;

internal sealed class MissingEmbeddedImageBinaryImportFailurePattern : IImportFailurePattern
{
    public const string Code = "WORKITEMS_PREPARE_MISSING_EMBEDDED_IMAGE_BINARY";

    public string PatternCode => Code;

    public async Task<IReadOnlyList<ImportFailureFinding>> EvaluateAsync(
        ImportFailurePatternContext context,
        CancellationToken cancellationToken)
    {
        if (!context.WorkItemsOptions.Extensions.EmbeddedImages.Enabled)
        {
            return [];
        }

        var findings = new List<ImportFailureFinding>();
        var package = context.PrepareContext.Package;

        await foreach (var parsedRevision in WorkItemsPrepareRevisionReader.EnumerateAsync(
                           package,
                           cancellationToken).ConfigureAwait(false))
        {
            if (parsedRevision.Revision is null)
            {
                continue;
            }

            foreach (var image in parsedRevision.Revision.EmbeddedImages)
            {
                if (string.IsNullOrWhiteSpace(image.RelativePath))
                {
                    findings.Add(new ImportFailureFinding(
                        PatternCode,
                        ImportFailureSeverity.Blocking,
                        $"{parsedRevision.RevisionFolderPath}/embeddedImages",
                        "Embedded image metadata has an empty relativePath.",
                        "Correct the embedded image metadata and re-run Prepare."));
                    continue;
                }

                var imagePath = $"{parsedRevision.RevisionFolderPath}/{image.RelativePath}";
                if (!await package.ContentExistsAsync(
                        new PackageContentContext(PackageContentKind.Artefact, Address: new RelativePathAddress(imagePath)),
                        cancellationToken).ConfigureAwait(false))
                {
                    findings.Add(new ImportFailureFinding(
                        PatternCode,
                        ImportFailureSeverity.Blocking,
                        imagePath,
                        $"Embedded image binary missing from package: {imagePath}",
                        "Re-run export with embedded images enabled and verify the image binary exists in the revision folder."));
                }
            }
        }

        return findings;
    }

    private sealed class RelativePathAddress(string relativePath) : IPackageContentAddress
    {
        public string RelativePath => relativePath.Replace('\\', '/').TrimStart('/');
    }
}

