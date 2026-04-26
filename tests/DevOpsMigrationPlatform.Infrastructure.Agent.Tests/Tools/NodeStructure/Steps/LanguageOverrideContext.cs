using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeStructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.NodeStructure.Steps;

public class LanguageOverrideContext
{
    public string? AreaLanguageOverride { get; set; }
    public string? IterationLanguageOverride { get; set; }
    public string? TranslatedAreaPath { get; private set; }
    public string? TranslatedIterationPath { get; private set; }
    public string SourceProjectName { get; set; } = "SourceProject";
    public string TargetProjectName { get; set; } = "TargetProject";

    public void TranslateAreaPath(string sourcePath)
    {
        var tool = BuildTool();
        var mapping = new ProjectMapping(SourceProjectName, TargetProjectName);
        var result = tool.TranslatePath("System.AreaPath", sourcePath, mapping);
        TranslatedAreaPath = result.TargetPath;
    }

    public void TranslateIterationPath(string sourcePath)
    {
        var tool = BuildTool();
        var mapping = new ProjectMapping(SourceProjectName, TargetProjectName);
        var result = tool.TranslatePath("System.IterationPath", sourcePath, mapping);
        TranslatedIterationPath = result.TargetPath;
    }

    private NodeStructureTool BuildTool()
        => new NodeStructureTool(
            Options.Create(new NodeStructureOptions
            {
                Enabled = true,
                AreaLanguageOverride = AreaLanguageOverride,
                IterationLanguageOverride = IterationLanguageOverride,
                AreaPathMappings = new List<NodeMapping>(),
                IterationPathMappings = new List<NodeMapping>()
            }),
            NullLogger<NodeStructureTool>.Instance);
}
