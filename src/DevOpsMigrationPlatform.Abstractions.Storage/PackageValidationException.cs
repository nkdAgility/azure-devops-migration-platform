// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;

namespace DevOpsMigrationPlatform.Abstractions.Storage;

/// <summary>
/// Signals deterministic validation failures at the package boundary.
/// </summary>
public sealed class PackageValidationException : Exception
{
    public string Code { get; }

    public PackageValidationException(string code, string message)
        : base(message)
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
    }

    public PackageValidationException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
    }
}

