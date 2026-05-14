// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Import.FailurePatterns;

internal sealed class MissingAttachmentBinaryImportFailurePattern : IImportFailurePattern
{
    public const string Code = "WORKITEMS_PREPARE_MISSING_ATTACHMENT_BINARY";

    public string PatternCode => Code;

    public async Task<IReadOnlyList<ImportFailureFinding>> EvaluateAsync(
        ImportFailurePatternContext context,
        CancellationToken cancellationToken)
    {
        if (!context.WorkItemsOptions.Extensions.Attachments.Enabled)
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

            foreach (var attachment in parsedRevision.Revision.Attachments)
            {
                if (string.IsNullOrWhiteSpace(attachment.RelativePath))
                {
                    findings.Add(new ImportFailureFinding(
                        PatternCode,
                        ImportFailureSeverity.Blocking,
                        $"{parsedRevision.RevisionFolderPath}/attachments",
                        "Attachment metadata has an empty relativePath.",
                        "Correct the attachment metadata and re-run Prepare."));
                    continue;
                }

                var attachmentPath = $"{parsedRevision.RevisionFolderPath}/{attachment.RelativePath}";
                if (!await artefactStore.ExistsAsync(attachmentPath, cancellationToken).ConfigureAwait(false))
                {
                    findings.Add(new ImportFailureFinding(
                        PatternCode,
                        ImportFailureSeverity.Blocking,
                        attachmentPath,
                        $"Attachment binary missing from package: {attachmentPath}",
                        "Re-run export with attachment replay enabled and verify the binary exists in the revision folder."));
                }
            }
        }

        return findings;
    }
}

