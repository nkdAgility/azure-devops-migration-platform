// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;

/// <summary>
/// Assertion helpers for <see cref="JobSubmissionOutputResult"/>.
/// Each method uses <see cref="Assert"/> so failures report the actual output.
/// </summary>
internal static class JobSubmissionOutputAssertions
{
    /// <summary>Asserts the output contains the "Job ID" label and the job ID value.</summary>
    internal static JobSubmissionOutputResult ShouldContainJobId(this JobSubmissionOutputResult result)
    {
        Assert.IsTrue(
            result.RawOutput.Contains("Job ID", StringComparison.Ordinal),
            $"Expected 'Job ID' label in output.\nActual:\n{result.RawOutput}");
        Assert.IsTrue(
            result.RawOutput.Contains(result.JobId.ToString(), StringComparison.Ordinal),
            $"Expected job ID value '{result.JobId}' in output.\nActual:\n{result.RawOutput}");
        return result;
    }

    /// <summary>Asserts the output contains the resolved control plane URL.</summary>
    internal static JobSubmissionOutputResult ShouldContainControlPlaneUrl(this JobSubmissionOutputResult result)
    {
        Assert.IsTrue(
            result.RawOutput.Contains(result.ControlPlaneUrl, StringComparison.Ordinal),
            $"Expected control plane URL '{result.ControlPlaneUrl}' in output.\nActual:\n{result.RawOutput}");
        return result;
    }

    /// <summary>
    /// Asserts the "Job ID" label appears before the "Control" label in the output,
    /// enforcing the ordering requirement from SC-004.
    /// </summary>
    internal static JobSubmissionOutputResult ShouldShowJobIdBeforeControlPlaneUrl(this JobSubmissionOutputResult result)
    {
        var jobIdIndex = result.RawOutput.IndexOf("Job ID", StringComparison.Ordinal);
        var controlIndex = result.RawOutput.IndexOf("Control", StringComparison.Ordinal);
        Assert.IsTrue(
            jobIdIndex >= 0 && controlIndex >= 0 && jobIdIndex < controlIndex,
            $"Expected 'Job ID' line before 'Control' line.\nActual:\n{result.RawOutput}");
        return result;
    }

    /// <summary>
    /// Asserts the error output contains the attempted control plane URL.
    /// Used for the submission-failure scenario.
    /// </summary>
    internal static JobSubmissionOutputResult ShouldShowAttemptedUrlInErrorOutput(this JobSubmissionOutputResult result)
    {
        Assert.IsTrue(
            result.IsFailure,
            "Expected a failure result but the fixture was not arranged for failure.");
        Assert.IsTrue(
            result.RawOutput.Contains(result.ControlPlaneUrl, StringComparison.Ordinal),
            $"Expected attempted URL '{result.ControlPlaneUrl}' in error output.\nActual:\n{result.RawOutput}");
        return result;
    }
}
