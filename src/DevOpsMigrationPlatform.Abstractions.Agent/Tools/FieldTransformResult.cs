// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Tools;

/// <summary>Output of applying all transforms to a set of work item fields.</summary>
public sealed record FieldTransformResult(
    IReadOnlyDictionary<string, object?> Fields,
    IReadOnlyList<FieldTransformAction> Actions);
