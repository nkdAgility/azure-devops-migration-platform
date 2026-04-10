using System;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Lightweight job summary returned by <c>GET /jobs</c>.
/// Used by the TUI job list view.
/// </summary>
public sealed record JobSummary(
    Guid JobId,
    string Mode,
    string State,
    string SubmittedByUpn,
    DateTimeOffset SubmittedAt
);
