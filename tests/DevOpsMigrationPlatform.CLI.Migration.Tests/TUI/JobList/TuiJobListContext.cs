// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.CLI.Migration.Tests.TUI.JobDetail;
using DevOpsMigrationPlatform.CLI.Views;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.TUI.JobList;

/// <summary>
/// Test context builder for <see cref="TuiJobListView"/> DSL tests.
/// Seeds a <see cref="FakeControlPlaneClient"/> and provides builder methods,
/// action triggers, and assertion delegates.
/// </summary>
public sealed class TuiJobListContext : IDisposable
{
    // ── Fakes ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reuses the existing <see cref="FakeControlPlaneClient"/> from TUI/JobDetail/.
    /// Seed jobs via <c>Client.Jobs.Add(…)</c>.
    /// </summary>
    public FakeControlPlaneClient Client { get; } = new();

    // ── System under test ──────────────────────────────────────────────────────

    /// <summary>
    /// The job list view under test. Constructed with a test-controlled refresh
    /// interval so timer-driven tests do not wait 10 seconds.
    /// </summary>
    public TuiJobListView View { get; }

    // ── Construction ───────────────────────────────────────────────────────────

    /// <param name="refreshIntervalMs">
    /// Refresh period in milliseconds. Pass 50–100 in timer-driven tests to avoid
    /// waiting for the production 10 000 ms default.
    /// </param>
    public TuiJobListContext(int refreshIntervalMs = 10_000)
    {
        View = new TuiJobListView(Client, refreshIntervalMs);
    }

    // ── Builder methods ────────────────────────────────────────────────────────

    /// <summary>Adds a job to the fake client's job list.</summary>
    public TuiJobListContext WithJob(Guid jobId, string mode = "Migrate", string state = "Running")
    {
        Client.Jobs.Add(new JobSummary(jobId, mode, state, "test@example.com", DateTimeOffset.UtcNow));
        return this;
    }

    // ── Actions ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Calls <see cref="TuiJobListView.UpdateJobs"/> directly with the current fake client
    /// job list. Simulates the display path without waiting for the timer.
    /// </summary>
    public void TriggerUpdateJobs()
        => View.UpdateJobs(Client.Jobs);

    /// <summary>
    /// Replaces the current job list in the fake client (simulates state change on
    /// the control plane) without calling UpdateJobs. The next timer tick will pick up
    /// the new list when using a short <c>refreshIntervalMs</c>.
    /// </summary>
    public void ChangeJobsOnControlPlane(IReadOnlyList<JobSummary> updatedJobs)
    {
        Client.Jobs.Clear();
        Client.Jobs.AddRange(updatedJobs);
    }

    // ── Assertion delegates ────────────────────────────────────────────────────

    /// <inheritdoc cref="TuiJobListAssertions.AssertTableHasColumns"/>
    public void AssertTableHasColumns(params string[] expectedColumns)
        => TuiJobListAssertions.AssertTableHasColumns(View, expectedColumns);

    /// <inheritdoc cref="TuiJobListAssertions.AssertTableRowCount"/>
    public void AssertTableRowCount(int expected)
        => TuiJobListAssertions.AssertTableRowCount(View, expected);

    /// <inheritdoc cref="TuiJobListAssertions.AssertRowContains"/>
    public void AssertRowContains(int rowIndex, string text)
        => TuiJobListAssertions.AssertRowContains(View, rowIndex, text);

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose() => View.Dispose();
}
