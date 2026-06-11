// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using Spectre.Console;
using Spectre.Console.Testing;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;

/// <summary>
/// Fixture that arranges and captures job-submission output from
/// <see cref="DevOpsMigrationPlatform.CLI.Migration.Commands.ControlPlaneCommandBase{TSettings}.PrintJobSubmitted"/>
/// and the error-path surface.
/// </summary>
internal sealed class JobSubmissionOutputFixture
{
    private string _controlPlaneUrl = "http://localhost:5100";
    private Guid _jobId = Guid.NewGuid();

    // --- Arrange ---

    /// <summary>
    /// Configures the fixture for standalone mode.
    /// The control plane URL is set to the default <c>http://localhost:5100</c>,
    /// matching <c>ControlPlaneEndpointOptions.BaseUrl</c> when no override is supplied.
    /// </summary>
    public JobSubmissionOutputFixture WithStandaloneMode()
    {
        _controlPlaneUrl = "http://localhost:5100";
        return this;
    }

    /// <summary>
    /// Configures the fixture for remote (hosted) mode using the supplied URL,
    /// representing the value that would be resolved from <c>--url</c> or
    /// <c>MIGRATION_API_URL</c>.
    /// </summary>
    public JobSubmissionOutputFixture WithRemoteUrl(string url)
    {
        _controlPlaneUrl = url;
        return this;
    }

    /// <summary>
    /// Sets the job ID that the stubbed submission client returned.
    /// </summary>
    public JobSubmissionOutputFixture WithJobId(Guid jobId)
    {
        _jobId = jobId;
        return this;
    }

    // --- Act ---

    /// <summary>
    /// Invokes <c>PrintJobSubmitted</c> with the arranged values and returns
    /// a <see cref="JobSubmissionOutputResult"/> wrapping captured console output.
    /// Represents the success path (job accepted).
    /// </summary>
    public JobSubmissionOutputResult ActJobAccepted()
    {
        var console = new TestConsole();
        TestControlPlaneCommandBase.InvokePrintJobSubmitted(console, _jobId, _controlPlaneUrl);
        return new JobSubmissionOutputResult(console.Output, _jobId, _controlPlaneUrl, isFailure: false);
    }

    /// <summary>
    /// Simulates a submission failure where the system renders the attempted URL
    /// in error output. Writes a representative error line to a <see cref="TestConsole"/>
    /// and returns a <see cref="JobSubmissionOutputResult"/> wrapping that output.
    /// The error format mirrors what <c>ControlPlaneCommandBase{TSettings}</c>
    /// is expected to write when <c>IJobSubmissionClient</c> throws or returns failure.
    /// </summary>
    /// <remarks>
    /// This writes directly to <see cref="TestConsole"/> because no production
    /// error-rendering helper exists yet. When one does, delegate to it and update
    /// this fixture.
    /// </remarks>
    public JobSubmissionOutputResult ActSubmissionFailed(string? errorMessage = null)
    {
        var console = new TestConsole();
        var message = errorMessage ?? $"Submission failed. Control plane: {_controlPlaneUrl}";
        console.MarkupLine($"[red]Error:[/] {Markup.Escape(message)}");
        return new JobSubmissionOutputResult(console.Output, _jobId, _controlPlaneUrl, isFailure: true);
    }
}
