// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;

/// <summary>
/// Records telemetry for attachment download operations.
/// Used by the net481 TFS path where <c>IPlatformMetrics</c> (which requires <c>TagList</c>) is unavailable.
/// </summary>
public interface IAttachmentDownloadMetrics
{
    void RecordAttempt();
    void RecordSuccess();
    void RecordFailure();
    void RecordDuration(TimeSpan duration);
}
