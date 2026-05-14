// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Storage;

/// <summary>
/// Signals deterministic operation failures at the package boundary.
/// </summary>
public sealed class PackageOperationException : Exception
{
    public string Code { get; }

    public PackageOperationException(string code, string message)
        : base(message)
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
    }

    public PackageOperationException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
    }
}

