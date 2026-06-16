// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Revisions;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Attachments.ImportFailures;

internal sealed class MissingAttachmentBinaryImportFailurePattern : IImportFailurePattern
{
    public const string Code = "WORKITEMS_PREPARE_MISSING_ATTACHMENT_BINARY";

    public string PatternCode => Code;

    public async Task<IReadOnlyList<ImportFailureFinding>> EvaluateAsync(
        ImportFailurePatternContext context,
        CancellationToken cancellationToken)
    {
        // Attachments are always-on core behaviour — no enablement guard needed.
        var findings = new List<ImportFailureFinding>();
        var package = context.PrepareContext.Package;
        var organisation = context.Organisation;
        var project = context.Project;

        await foreach (var parsedRevision in WorkItemsPrepareRevisionReader.EnumerateAsync(
                           package,
                           organisation,
                           project,
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
                var withinModulePath = StripModulePrefix(attachmentPath, organisation, project);
                if (!await package.ContentExistsAsync(
                        new PackageContentContext(
                            PackageContentKind.Artefact,
                            Organisation: organisation,
                            Project: project,
                            Module: "WorkItems",
                            Address: new RelativePathAddress(withinModulePath)),
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

    private static string StripModulePrefix(string path, string organisation, string project)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');

        var scopedPrefix = $"{organisation}/{project}/WorkItems/";
        if (normalized.StartsWith(scopedPrefix, StringComparison.OrdinalIgnoreCase))
            return normalized.Substring(scopedPrefix.Length);

        const string barePrefix = "WorkItems/";
        if (normalized.StartsWith(barePrefix, StringComparison.OrdinalIgnoreCase))
            return normalized.Substring(barePrefix.Length);

        return normalized;
    }

}
