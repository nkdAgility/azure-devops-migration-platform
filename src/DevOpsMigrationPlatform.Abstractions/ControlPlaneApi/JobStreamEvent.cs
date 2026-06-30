// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Streaming;

namespace DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;

/// <summary>
/// A single event received by the CLI from <c>GET /jobs/{id}/stream</c>.
/// The stream multiplexes progress, diagnostics, and terminal signals into one SSE connection.
/// </summary>
public sealed record JobStreamEvent(
    long Seq,
    JobStreamEventKind Kind,
    ProgressEvent? Progress,
    DiagnosticLogRecord? Diagnostic,
    bool? Failed,
    string? FailureReason);

public enum JobStreamEventKind
{
    Progress,
    Diagnostic,
    Terminal
}
