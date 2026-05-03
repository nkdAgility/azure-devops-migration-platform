// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Validation;

public record ValidationResult
{
    public bool Passed { get; init; }
    public IReadOnlyList<ValidationError> Errors { get; init; } = Array.Empty<ValidationError>();

    public static ValidationResult Ok() => new() { Passed = true };
    public static ValidationResult Fail(IReadOnlyList<ValidationError> errors) =>
        new() { Passed = false, Errors = errors };
}
