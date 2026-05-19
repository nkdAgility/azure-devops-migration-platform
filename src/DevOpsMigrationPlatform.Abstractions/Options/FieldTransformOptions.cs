// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Root options for the field-transform tool.
/// Bound from <c>MigrationPlatform:Tools:FieldTransform</c>.
/// </summary>
#if NET7_0_OR_GREATER
public sealed class FieldTransformOptions : IConfigSection
#else
public sealed class FieldTransformOptions
#endif
{
    /// <summary>Configuration section path.</summary>
    public static string SectionName => "MigrationPlatform:Tools:FieldTransform";

    /// <summary>Whether the field-transform tool is active. Default: <c>false</c>.</summary>
    public bool Enabled { get; init; } = false;

    /// <summary>Ordered list of transform groups to apply.</summary>
    public IReadOnlyList<FieldTransformGroupOptions> TransformGroups { get; init; } = [];
}
