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

internal sealed class MissingEmbeddedImageBinaryImportFailurePattern : IImportFailurePattern
{
    public const string Code = "WORKITEMS_PREPARE_MISSING_EMBEDDED_IMAGE_BINARY";

    private readonly IWorkItemRevisionReader _revisionReader;

    public MissingEmbeddedImageBinaryImportFailurePattern(IWorkItemRevisionReader? revisionReader = null)
    {
        _revisionReader = revisionReader ?? new WorkItemsPrepareRevisionReader();
    }

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
        var organisation = context.Organisation;
        var project = context.Project;

        await foreach (var parsedRevision in _revisionReader.EnumerateAsync(
                           package,
                           organisation,
                           project,
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
                var withinModulePath = StripModulePrefix(imagePath, organisation, project);
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
                        imagePath,
                        $"Embedded image binary missing from package: {imagePath}",
                        "Re-run export with embedded images enabled and verify the image binary exists in the revision folder."));
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
