// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Storage;

internal sealed class PackagePathRouter
{
    private const string PrepareReportPath = ".migration/prepare-report.json";
    private const string JobDescriptorPath = ".migration/job.json";

    public string ResolveContentPath(PackageContext context)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));

        if (string.IsNullOrWhiteSpace(context.ContentKind))
            throw new PackageValidationException(
                "PKG_CONTENT_KIND_REQUIRED",
                "Content kind must be provided.");

        if (context.ContentKind.IndexOf('/') >= 0)
            return context.ContentKind;

        throw new PackageValidationException(
            "PKG_CONTENT_KIND_UNSUPPORTED",
            $"Unsupported content kind '{context.ContentKind}'.");
    }

    public string ResolveMetaPath(PackageMetaContext context, string? runId = null, bool runAudit = false)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));

        return context.Kind switch
        {
            PackageMetaKind.MigrationConfig => runAudit && !string.IsNullOrWhiteSpace(runId)
                ? PackagePaths.RunAuditConfigFile(runId!)
                : PackagePaths.MigrationConfigFileName,
            PackageMetaKind.ExecutionPlan => runAudit && !string.IsNullOrWhiteSpace(runId)
                ? PackagePaths.RunAuditPlanFile(runId!)
                : PackagePaths.PlanFile,
            PackageMetaKind.PhaseRecord => PackagePaths.PhaseFile,
            PackageMetaKind.InventoryCompletionMarker => PackagePaths.InventoryCompleteFile,
            PackageMetaKind.PrepareReport => PrepareReportPath,
            PackageMetaKind.JobDescriptor => !string.IsNullOrWhiteSpace(runId)
                ? PackagePaths.RunJobFile(runId!)
                : JobDescriptorPath,
            PackageMetaKind.CheckpointCursor => throw new PackageOperationException(
                "PKG_META_KIND_CONTEXT_REQUIRED",
                "Checkpoint cursor routing requires action/module context and is not supported by the base router."),
            PackageMetaKind.ContinuationToken => throw new PackageOperationException(
                "PKG_META_KIND_CONTEXT_REQUIRED",
                "Continuation token routing requires action/module context and is not supported by the base router."),
            _ => throw new PackageOperationException(
                "PKG_META_KIND_UNSUPPORTED",
                $"Unsupported metadata kind '{context.Kind}'.")
        };
    }

    public string ResolveLogPath(PackageLogContext context)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));
        if (string.IsNullOrWhiteSpace(context.RunId))
            throw new PackageValidationException(
                "PKG_RUN_ID_REQUIRED",
                "Run ID must be provided.");

        var fileName = context.Stream switch
        {
            PackageLogStream.Progress => "progress.ndjson",
            PackageLogStream.Diagnostics => "diagnostics.ndjson",
            _ => throw new PackageOperationException(
                "PKG_LOG_STREAM_UNSUPPORTED",
                $"Unsupported log stream '{context.Stream}'.")
        };

        return $"{PackagePaths.RunLogsFolder(context.RunId)}/{fileName}";
    }
}

