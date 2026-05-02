// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using Microsoft.Extensions.Logging;
using Moq;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.SchemaValidation;

public sealed class SchemaValidationContext
{
    public string? TempConfigPath { get; set; }
    public string? TempSchemaPath { get; set; }
    public int? ExitCode { get; set; }
    public Mock<IJobSubmissionClient>? MockControlPlaneClient { get; set; }
    public List<(LogLevel Level, string Message, Dictionary<string, object> State)> CapturedLogs { get; } = new();
    public string? ConfigContent { get; set; }
    public bool SchemaFileAbsent { get; set; }

    public void Cleanup()
    {
        if (TempConfigPath != null && File.Exists(TempConfigPath))
        {
            File.Delete(TempConfigPath);
        }

        if (TempSchemaPath != null && File.Exists(TempSchemaPath))
        {
            File.Delete(TempSchemaPath);
        }
    }
}
