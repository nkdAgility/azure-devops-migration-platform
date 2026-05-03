// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Platform;

public class ConfigValidationContext
{
    // The options under test
    public MigrationOptions Options { get; set; } = new();

    // Captured result from invoking the validator
    public ValidateOptionsResult? Result { get; set; }

    // The system under test — internal visibility granted via InternalsVisibleTo
    internal MigrationOptionsValidator Sut { get; } = new();
}
