// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Storage;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.FailurePatterns;

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
        var package = context.PrepareContext.Package;

        await foreach (var parsedRevision in WorkItemsPrepareRevisionReader.EnumerateAsync(
                           package,
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
                if (!await package.ContentExistsAsync(
                        new PackageContentContext(PackageContentKind.Artefact, Address: new RelativePathAddress(attachmentPath)),
                        cancellationToken).ConfigureAwait(false))
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

    private sealed class RelativePathAddress(string relativePath) : IPackageContentAddress
    {
        public string RelativePath => relativePath.Replace('\\', '/').TrimStart('/');
    }
}

