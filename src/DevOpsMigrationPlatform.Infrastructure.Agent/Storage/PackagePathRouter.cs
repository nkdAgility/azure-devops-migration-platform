// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Storage;

internal sealed class PackagePathRouter
{
    private const string PrepareReportPath = ".migration/prepare-report.json";
    private const string JobDescriptorPath = ".migration/job.json";

    public string ResolveContentPath(PackageContentContext context)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));
        return context.Kind switch
        {
            PackageContentKind.Artefact => ResolveAddressedPath(context, isCollection: false),
            PackageContentKind.Collection => ResolveAddressedPath(context, isCollection: true),
            PackageContentKind.Manifest => ResolveManifestPath(context),
            _ => throw new PackageValidationException(
                "PKG_KIND_UNSUPPORTED",
                $"Unsupported package kind '{context.Kind}'.")
        };
    }

    private static string ResolveManifestPath(PackageContentContext context)
    {
        var segments = new List<string>(capacity: 3);
        AddSegment(segments, context.Organisation);
        AddSegment(segments, context.Project);

        if (segments.Count != 2)
        {
            throw new PackageValidationException(
                "PKG_MANIFEST_SCOPE_REQUIRED",
                "Manifest routing requires organisation and project scope.");
        }

        if (!string.IsNullOrWhiteSpace(context.Module) || context.Address is not null)
        {
            throw new PackageValidationException(
                "PKG_MANIFEST_SCOPE_INVALID",
                "Manifest routing does not allow module scope or a content address.");
        }

        segments.Add("manifest.json");
        return string.Join("/", segments);
    }

    private static string ResolveAddressedPath(PackageContentContext context, bool isCollection)
    {
        var segments = new List<string>(capacity: 4);
        AddSegment(segments, context.Organisation);
        AddSegment(segments, context.Project);
        AddSegment(segments, context.Module);

        var addressPath = NormalizeAddressPath(context.Address?.RelativePath, isCollection);

        if (string.IsNullOrEmpty(addressPath))
        {
            if (segments.Count == 0 && isCollection)
                return string.Empty;

            if (segments.Count > 0)
                return isCollection ? $"{string.Join("/", segments)}/" : string.Join("/", segments);

            throw new PackageValidationException(
                "PKG_ROUTE_REQUIRED",
                "Package content routing requires scope or a content address.");
        }

        if (segments.Count == 0)
            return isCollection ? EnsureTrailingSlash(addressPath) : addressPath;

        var prefix = string.Join("/", segments);
        var combined = $"{prefix}/{addressPath}";
        return isCollection ? EnsureTrailingSlash(combined) : combined;
    }

    private static void AddSegment(List<string> segments, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        var segment = value!;
        if (segment.IndexOf('/') >= 0)
            throw new PackageValidationException(
                "PKG_ROUTE_SEGMENT_INVALID",
                "Package route segments must not contain '/'.");
        segments.Add(segment);
    }

    private static string NormalizeAddressPath(string? value, bool isCollection)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var normalized = value!.Replace('\\', '/').Trim();
        while (normalized.StartsWith("/", StringComparison.Ordinal))
            normalized = normalized.Substring(1);

        if (normalized.IndexOf(":", StringComparison.Ordinal) >= 0
            || normalized.Split('/').Any(segment => segment == ".."))
        {
            throw new PackageValidationException(
                "PKG_ADDRESS_INVALID",
                "Package content addresses must be relative and must not escape the package scope.");
        }

        normalized = normalized.TrimEnd('/');
        if (string.IsNullOrEmpty(normalized) && !isCollection)
        {
            throw new PackageValidationException(
                "PKG_ADDRESS_REQUIRED",
                "Package content addresses must not be empty for single artefact requests.");
        }

        return normalized;
    }

    private static string EnsureTrailingSlash(string path)
        => string.IsNullOrEmpty(path) || path.EndsWith("/", StringComparison.Ordinal) ? path : $"{path}/";

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

