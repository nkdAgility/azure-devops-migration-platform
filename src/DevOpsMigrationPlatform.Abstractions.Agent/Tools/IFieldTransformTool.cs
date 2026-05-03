// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Tools;

/// <summary>
/// Applies all configured transform groups to a work item's fields for a given pipeline phase.
/// This is the single entry-point for field transformation within the migration pipeline.
/// </summary>
public interface IFieldTransformTool
{
    /// <summary>Applies all enabled transforms for the current phase and returns the mutated field set.</summary>
    FieldTransformResult ApplyTransforms(IReadOnlyDictionary<string, object?> fields, FieldTransformContext context);

    /// <summary>Returns <c>true</c> if at least one transform group is active for <paramref name="phase"/>.</summary>
    bool IsEnabledForPhase(FieldTransformPhase phase);
}
