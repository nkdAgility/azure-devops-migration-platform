// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Context;

/// <summary>
/// Concrete implementation of <see cref="IAgentJobContext"/>.
/// Validates that PackagePath is absolute and Mode is one of the known runtime values.
/// </summary>
public sealed class AgentJobContext : IAgentJobContext
{
    private static readonly HashSet<string> ValidModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Inventory", "Dependencies", "Export", "Import", "Prepare", "Migrate"
    };

    private readonly ILogger<AgentJobContext>? _logger;

    private string _mode = string.Empty;
    private string _packagePath = string.Empty;
    private string _configVersion = string.Empty;

    public AgentJobContext(ILogger<AgentJobContext>? logger = null)
    {
        _logger = logger;
    }

    public required string Mode
    {
        get => _mode;
        init
        {
            if (!ValidModes.Contains(value))
            {
                throw new InvalidOperationException(
                    $"Invalid Mode '{value}'. Must be one of: Inventory, Dependencies, Export, Import, Prepare, Migrate.");
            }
            _mode = value;

            if (!string.IsNullOrEmpty(_mode) && !string.IsNullOrEmpty(_configVersion))
            {
                _logger?.LogDebug("Agent job context resolved — Mode={Mode} ConfigVersion={ConfigVersion}", _mode, _configVersion);
            }
        }
    }

    public required string PackagePath
    {
        get => _packagePath;
        init
        {
            if (!IsAbsolutePackagePath(value))
            {
                throw new InvalidOperationException(
                    $"PackagePath must be an absolute path. Received: '{value}'");
            }
            _packagePath = value;
        }
    }

    public required string ConfigVersion
    {
        get => _configVersion;
        init
        {
            _configVersion = value;
            if (!string.IsNullOrEmpty(_mode) && !string.IsNullOrEmpty(_configVersion))
            {
                _logger?.LogDebug("Agent job context resolved — Mode={Mode} ConfigVersion={ConfigVersion}", _mode, _configVersion);
            }
        }
    }

    private static bool IsAbsolutePackagePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value[0] == '/')
        {
            return true;
        }

        if (value.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return true;
        }

        return value.Length >= 3
            && char.IsLetter(value[0])
            && value[1] == ':'
            && (value[2] == '\\' || value[2] == '/');
    }
}
