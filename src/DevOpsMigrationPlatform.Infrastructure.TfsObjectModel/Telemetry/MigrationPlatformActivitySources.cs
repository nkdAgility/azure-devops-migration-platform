// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Diagnostics;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Telemetry;

/// <summary>
/// Shared <see cref="ActivitySource"/> instances for the TFS export subprocess.
/// </summary>
public static class MigrationPlatformActivitySources
{
    public static readonly ActivitySource WorkItemExport = new ActivitySource("DevOpsMigrationPlatform.WorkItemExport");
    public static readonly ActivitySource WorkItem = new ActivitySource("DevOpsMigrationPlatform.WorkItem");
    public static readonly ActivitySource AttachmentDownload = new ActivitySource("DevOpsMigrationPlatform.AttachmentDownload");
    public static readonly ActivitySource GitMigration = new ActivitySource("DevOpsMigrationPlatform.GitMigration");
}
