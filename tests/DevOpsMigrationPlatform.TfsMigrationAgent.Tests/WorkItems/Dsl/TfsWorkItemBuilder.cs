// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.WorkItems.Revisions;

namespace DevOpsMigrationPlatform.TfsMigrationAgent.Tests.WorkItems.Dsl;

/// <summary>
/// Builds field dictionaries for work-item stubs used with <see cref="TfsWorkItemFetchHarness"/>.
/// Fluent; each method returns the builder.
/// </summary>
internal sealed class TfsWorkItemBuilder
{
    private int _id;
    private readonly Dictionary<string, object?> _fields = new Dictionary<string, object?>();

    public static TfsWorkItemBuilder ForId(int id) => new TfsWorkItemBuilder { _id = id };

    public TfsWorkItemBuilder WithField(string name, object? value)
    {
        _fields[name] = value;
        return this;
    }

    public TfsWorkItemBuilder WithType(string workItemType) =>
        WithField("System.WorkItemType", workItemType);

    public TfsWorkItemBuilder WithState(string state) =>
        WithField("System.State", state);

    public TfsWorkItemBuilder WithTitle(string title) =>
        WithField("System.Title", title);

    /// <summary>Returns the (id, fields) tuple for registration in the harness.</summary>
    public (int id, Dictionary<string, object?> fields) Build() =>
        (_id, new Dictionary<string, object?>(_fields));
}
