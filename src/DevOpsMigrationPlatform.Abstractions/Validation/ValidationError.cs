// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Validation;

public record ValidationError
{
    /// <summary>Package-relative path to the offending folder or file.</summary>
    public string Path { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;
}
