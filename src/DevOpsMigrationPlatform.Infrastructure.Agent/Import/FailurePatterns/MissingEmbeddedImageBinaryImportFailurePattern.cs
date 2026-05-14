// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Import.FailurePatterns;

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
        var artefactStore = context.PrepareContext.ArtefactStore;

        await foreach (var parsedRevision in WorkItemsPrepareRevisionReader.EnumerateAsync(
                           artefactStore,
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
                if (!await artefactStore.ExistsAsync(imagePath, cancellationToken).ConfigureAwait(false))
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
}

