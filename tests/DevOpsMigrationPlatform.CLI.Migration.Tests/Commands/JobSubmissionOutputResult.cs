// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;

/// <summary>
/// Captures console output and the context values from a job-submission act.
/// </summary>
internal sealed class JobSubmissionOutputResult
{
    internal string RawOutput { get; }
    internal Guid JobId { get; }
    internal string ControlPlaneUrl { get; }
    internal bool IsFailure { get; }

    internal JobSubmissionOutputResult(
        string rawOutput,
        Guid jobId,
        string controlPlaneUrl,
        bool isFailure)
    {
        RawOutput = rawOutput;
        JobId = jobId;
        ControlPlaneUrl = controlPlaneUrl;
        IsFailure = isFailure;
    }
}
