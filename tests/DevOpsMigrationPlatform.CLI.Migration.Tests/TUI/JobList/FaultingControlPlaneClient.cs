// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.TUI.JobList;

/// <summary>
/// <see cref="IControlPlaneClient"/> that throws <see cref="HttpRequestException"/> from
/// <see cref="GetAllJobsAsync"/>. Used to exercise the TuiCommand error path (S3) and
/// default-URL advisory (S4).
/// </summary>
internal sealed class FaultingControlPlaneClient : IControlPlaneClient
{
    /// <summary>The URL string embedded in the exception message.</summary>
    public string? AttemptedUrl { get; set; }

    public Task<IReadOnlyList<JobSummary>> GetAllJobsAsync(CancellationToken ct)
        => throw new HttpRequestException($"No connection could be made to {AttemptedUrl ?? "unknown"}");

    public async IAsyncEnumerable<JobStreamEvent> StreamJobAsync(
        Guid jobId,
        [EnumeratorCancellation] CancellationToken ct,
        long fromSeq = 0)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

    public Task<JobBootstrap?> GetBootstrapAsync(Guid jobId, CancellationToken ct)
        => throw new NotSupportedException();

    public Task<JobTaskList?> GetTasksAsync(Guid jobId, CancellationToken ct)
        => throw new NotSupportedException();
}
