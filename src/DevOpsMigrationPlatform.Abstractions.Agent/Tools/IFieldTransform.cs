// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Tools;

/// <summary>
/// Applies a single named transformation to a set of work item fields.
/// Implementations are registered by <see cref="IFieldTransformFactory"/> and composed by <see cref="IFieldTransformTool"/>.
/// </summary>
public interface IFieldTransform
{
    /// <summary>Discriminator used to resolve this transform from configuration.</summary>
    string Type { get; }

    /// <summary>Human-readable name of this transform instance.</summary>
    string Name { get; }

    /// <summary>
    /// Applies the transform and returns the mutated field set plus an audit log of actions taken.
    /// Must not modify <paramref name="fields"/> in-place; return a new dictionary in <see cref="FieldTransformResult.Fields"/>.
    /// </summary>
    FieldTransformResult Apply(IReadOnlyDictionary<string, object?> fields, FieldTransformContext context);
}
