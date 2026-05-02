// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) NKD Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;
using Microsoft.Extensions.Options;
using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.NodeTranslation.Steps;

/// <summary>Shared scenario state for path mapping BDD tests.</summary>
public class PathMappingContext
{
    public string SourceProjectName { get; set; } = "SourceProject";
    public string TargetProjectName { get; set; } = "TargetProject";
    public List<NodeMapping> AreaMappings { get; } = new();
    public List<NodeMapping> IterationMappings { get; } = new();
    public bool Enabled { get; set; } = true;
    public PathTranslation? Result { get; set; }

    private NodeTranslationTool? _tool;

    public NodeTranslationTool GetTool()
    {
        if (_tool == null)
        {
            var opts = new NodeTranslationOptions
            {
                Enabled = Enabled,
                AreaPathMappings = AreaMappings.AsReadOnly(),
                IterationPathMappings = IterationMappings.AsReadOnly()
            };
            _tool = new NodeTranslationTool(Options.Create(opts));
        }
        return _tool;
    }

    public ProjectMapping GetMapping() => new(SourceProjectName, TargetProjectName);
}
