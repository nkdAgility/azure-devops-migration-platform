// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Attachments.ImportFailures;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Revisions.ImportFailures;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.WorkItemResolution;

/// <summary>
/// Orchestrates WorkItems prepare-time import validation checks and report composition.
/// </summary>
public sealed class ImportPreparer
{
    private const string ModuleName = "WorkItems";

    private readonly WorkItemsModuleOptions _workItemsOptions;
    private readonly IReadOnlyList<IImportFailurePattern> _importFailurePatterns;
    private readonly string _organisation;
    private readonly string _project;

    public ImportPreparer(
        IOptions<WorkItemsModuleOptions> workItemsOptions,
        string organisation,
        string project,
        IEnumerable<IImportFailurePattern>? importFailurePatterns = null)
    {
        _workItemsOptions = workItemsOptions.Value;
        _organisation = organisation ?? throw new ArgumentNullException(nameof(organisation));
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _importFailurePatterns = importFailurePatterns?.ToArray() ?? [];
    }

    public async Task<PrepareReport> PrepareAsync(PrepareContext context, CancellationToken cancellationToken)
    {
        var importFailurePatternContext = new ImportFailurePatternContext(context, _workItemsOptions, _organisation, _project);
        var failureFindings = new List<ImportFailureFinding>();
        foreach (var pattern in _importFailurePatterns)
        {
            var findings = await pattern.EvaluateAsync(importFailurePatternContext, cancellationToken).ConfigureAwait(false);
            if (findings.Count > 0)
            {
                failureFindings.AddRange(findings);
            }
        }

        var readiness = failureFindings.Any(f => f.Severity == ImportFailureSeverity.Blocking)
            ? WorkItemsPrepareReadinessResult.ChangesRequired
            : WorkItemsPrepareReadinessResult.Ready;

        var unresolvedItems = failureFindings
            .Select(f => new UnresolvedItem(
                f.EvidenceKey,
                $"{f.PatternCode}: {f.Message}",
                f.Severity == ImportFailureSeverity.Blocking ? PrepareIssueSeverity.Blocking : PrepareIssueSeverity.Warning))
            .ToList();

        var artefactFindings = MapArtefactFindings(failureFindings);
        var fieldTransformFindings = MapFieldTransformFindings(failureFindings);
        var importReadinessReport = ImportReadinessReport.Create(
            readiness,
            failureFindings,
            artefactFindings,
            fieldTransformFindings);

        var resolvedCount = await CountRevisionArtefactsAsync(context, cancellationToken).ConfigureAwait(false);
        return new PrepareReport
        {
            ModuleName = ModuleName,
            ResolvedCount = resolvedCount,
            UnresolvedItems = unresolvedItems,
            ArtefactFindings = artefactFindings,
            FieldTransformFindings = fieldTransformFindings,
            Readiness = readiness,
            ImportReadinessReport = importReadinessReport,
            FailureFindings = failureFindings
        };
    }

    private static IReadOnlyList<ArtefactFinding> MapArtefactFindings(IReadOnlyList<ImportFailureFinding> failureFindings)
    {
        var findings = new List<ArtefactFinding>();
        foreach (var failureFinding in failureFindings)
        {
            if (failureFinding.PatternCode == MissingRevisionArtefactImportFailurePattern.Code)
            {
                findings.Add(new ArtefactFinding(
                    ArtefactFindingType.RevisionFolder,
                    ModuleName,
                    ArtefactFindingStatus.Missing,
                    failureFinding.EvidenceKey));
            }
            else if (failureFinding.PatternCode == InvalidRevisionPayloadImportFailurePattern.Code)
            {
                findings.Add(new ArtefactFinding(
                    ArtefactFindingType.RevisionFolder,
                    failureFinding.EvidenceKey,
                    ArtefactFindingStatus.Invalid,
                    failureFinding.EvidenceKey));
            }
            else if (failureFinding.PatternCode == MissingAttachmentBinaryImportFailurePattern.Code)
            {
                findings.Add(new ArtefactFinding(
                    ArtefactFindingType.Attachment,
                    failureFinding.EvidenceKey,
                    ArtefactFindingStatus.Missing,
                    failureFinding.EvidenceKey));
            }
            else if (failureFinding.PatternCode == MissingEmbeddedImageBinaryImportFailurePattern.Code)
            {
                findings.Add(new ArtefactFinding(
                    ArtefactFindingType.EmbeddedImage,
                    failureFinding.EvidenceKey,
                    ArtefactFindingStatus.Missing,
                    failureFinding.EvidenceKey));
            }
        }

        return findings;
    }

    private static IReadOnlyList<FieldTransformFinding> MapFieldTransformFindings(IReadOnlyList<ImportFailureFinding> failureFindings)
    {
        var findings = new List<FieldTransformFinding>();
        foreach (var failureFinding in failureFindings.Where(f => f.PatternCode == FieldTransformCompatibilityImportFailurePattern.Code))
        {
            var segments = failureFinding.EvidenceKey.Split('|');
            if (segments.Length >= 4
                && Enum.TryParse(segments[0], ignoreCase: true, out FieldTransformFindingStatus status))
            {
                findings.Add(new FieldTransformFinding(
                    segments[2],
                    segments[3],
                    segments[1],
                    status,
                    failureFinding.SuggestedAction));
                continue;
            }

            findings.Add(new FieldTransformFinding(
                failureFinding.EvidenceKey,
                "Unknown",
                failureFinding.PatternCode,
                FieldTransformFindingStatus.Error,
                failureFinding.SuggestedAction));
        }

        return findings;
    }

    private async Task<int> CountRevisionArtefactsAsync(PrepareContext context, CancellationToken cancellationToken)
    {
        var resolvedCount = 0;
        await foreach (var artefactPath in context.Package.EnumerateContentAsync(
                           new PackageContentContext(PackageContentKind.Collection, Organisation: _organisation, Project: _project, Module: ModuleName, IsCollectionRequest: true),
                           cancellationToken).ConfigureAwait(false))
        {
            if (artefactPath.EndsWith("/revision.json", StringComparison.Ordinal))
            {
                resolvedCount++;
            }
        }

        return resolvedCount;
    }
}
