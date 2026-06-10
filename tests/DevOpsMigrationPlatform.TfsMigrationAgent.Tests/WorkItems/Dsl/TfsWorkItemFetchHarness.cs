// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.WorkItems.Revisions;
using Moq;

namespace DevOpsMigrationPlatform.TfsMigrationAgent.Tests.WorkItems.Dsl;

/// <summary>
/// Test harness for <see cref="TfsWorkItemFetchService"/> field-projection and filter scenarios.
/// Provides a fluent API for registering work-item stubs and executing the SUT without a
/// real TFS connection.
/// </summary>
internal sealed class TfsWorkItemFetchHarness
{
    private readonly List<(int id, Dictionary<string, object?> fields)> _workItems =
        new List<(int id, Dictionary<string, object?> fields)>();

    // ── Fluent factory ─────────────────────────────────────────────────────────

    public static TfsWorkItemFetchHarness Create() => new TfsWorkItemFetchHarness();

    /// <summary>
    /// Registers a work item stub with the given id and field values.
    /// All field names/values become available for projection and filtering.
    /// </summary>
    public TfsWorkItemFetchHarness WithWorkItem(int id, Dictionary<string, object?> fields)
    {
        _workItems.Add((id, fields));
        return this;
    }

    /// <summary>
    /// Overload that accepts a pre-built (id, fields) tuple from <see cref="TfsWorkItemBuilder.Build"/>.
    /// </summary>
    public TfsWorkItemFetchHarness WithWorkItem((int id, Dictionary<string, object?> fields) item) =>
        WithWorkItem(item.id, item.fields);

    // ── SUT execution ──────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the SUT with a stubbed <see cref="IWorkItemQueryWindowStrategy"/> returning a
    /// single window containing all registered work-item IDs, and a stubbed
    /// <see cref="IWorkItemFieldReader"/> returning the registered field values.
    /// Calls <c>FetchAsync</c> with <paramref name="scope"/> and collects all yielded items.
    /// </summary>
    public async Task<IReadOnlyList<FetchedWorkItem>> FetchAllAsync(
        WorkItemFetchScope scope,
        CancellationToken ct = default)
    {
        var windowStrategy = BuildWindowStrategyStub();
        var fieldReader = BuildFieldReaderStub();
        var sut = new TfsWorkItemFetchService(fieldReader, windowStrategy);

        var results = new List<FetchedWorkItem>();
        await foreach (var item in sut.FetchAsync(new OrganisationEndpoint(), "TestProject", scope, ct)
                                      .ConfigureAwait(false))
            results.Add(item);

        return results.AsReadOnly();
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private IWorkItemQueryWindowStrategy BuildWindowStrategyStub()
    {
        var ids = new int[_workItems.Count];
        for (int i = 0; i < _workItems.Count; i++)
            ids[i] = _workItems[i].id;

        var mock = new Mock<IWorkItemQueryWindowStrategy>(MockBehavior.Strict);
        mock.Setup(s => s.EnumerateWindowsAsync(
                It.IsAny<OrganisationEndpoint>(),
                It.IsAny<string>(),
                It.IsAny<WorkItemQueryWindowOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns(SingleWindowStream(ids));
        return mock.Object;
    }

    private IWorkItemFieldReader BuildFieldReaderStub()
    {
        var mock = new Mock<IWorkItemFieldReader>(MockBehavior.Strict);
        foreach (var (id, fields) in _workItems)
        {
            var captured = fields;
            mock.Setup(r => r.GetFields(id))
                .Returns(captured);
        }
        return mock.Object;
    }

    private static async IAsyncEnumerable<WorkItemQueryWindow> SingleWindowStream(
        int[] ids,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield return new WorkItemQueryWindow { WorkItemIds = ids };
    }
}
