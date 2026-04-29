using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.NodeTranslation.Steps;

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

    private NodeTranslationTool BuildTool()
        => new NodeTranslationTool(
            Options.Create(new NodeTranslationOptions
            {
                Enabled = true,
                AreaLanguageOverride = AreaLanguageOverride,
                IterationLanguageOverride = IterationLanguageOverride,
                AreaPathMappings = new List<NodeMapping>(),
                IterationPathMappings = new List<NodeMapping>()
            }),
            NullLogger<NodeTranslationTool>.Instance);
}
