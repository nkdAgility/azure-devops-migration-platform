// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.RuntimeState;

internal sealed class RuntimeStateTestContext
{
    public const string OrganisationUrl = "https://dev.azure.com/contoso";
    public const string Project = "FabrikamWeb";
    public static readonly DateTimeOffset Timestamp = new(2026, 05, 07, 10, 0, 0, TimeSpan.Zero);

    public static CursorEntry Cursor(string path, string stage = CursorStage.Completed) => new()
    {
        LastProcessed = path,
        Stage = stage,
        UpdatedAt = Timestamp
    };
}
