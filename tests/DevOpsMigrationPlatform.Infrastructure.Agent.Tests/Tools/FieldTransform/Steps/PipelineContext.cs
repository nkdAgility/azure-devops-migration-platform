using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform.Steps;

/// <summary>
/// Shared scenario state for field transform pipeline filtering and enabled-flag scenarios.
/// Maintains mutable builder state; call BuildTool to materialise.
/// </summary>
public class PipelineContext
{
    public bool ToolEnabled { get; set; } = true;
    private readonly List<GroupDef> _groups = new List<GroupDef>();
    public Dictionary<string, object?> InputFields { get; set; } = new Dictionary<string, object?>();
    public FieldTransformResult? Result { get; set; }
    public Exception? ExceptionCaught { get; set; }
    public bool? IsEnabledResult { get; set; }

    public void AddGroup(bool groupEnabled, IEnumerable<RuleDef> rules)
        => _groups.Add(new GroupDef(groupEnabled, new List<RuleDef>(rules)));

    public void SetLastGroupSecondRuleDisabled()
    {
        var group = _groups[_groups.Count - 1];
        var rule = group.Rules[1];
        group.Rules[1] = new RuleDef(rule.Type, rule.Field, rule.Value, enabled: false);
    }

    private FieldTransformOptions BuildOptions()
    {
        var groups = new List<FieldTransformGroupOptions>();
        foreach (var g in _groups)
        {
            var transforms = new List<FieldTransformRuleOptions>();
            foreach (var r in g.Rules)
                transforms.Add(new FieldTransformRuleOptions { Type = r.Type, Field = r.Field, Value = r.Value, Enabled = r.Enabled });
            groups.Add(new FieldTransformGroupOptions { Enabled = g.Enabled, Transforms = transforms });
        }
        return new FieldTransformOptions { Enabled = ToolEnabled, TransformGroups = groups };
    }

    public FieldTransformTool BuildTool()
    {
        var options = BuildOptions();
        var factory = new FieldTransformFactory(NullLoggerFactory.Instance);
        return new FieldTransformTool(Options.Create(options), factory, NullLoggerFactory.Instance);
    }

    public sealed class GroupDef
    {
        public bool Enabled { get; }
        public List<RuleDef> Rules { get; }
        public GroupDef(bool enabled, List<RuleDef> rules) { Enabled = enabled; Rules = rules; }
    }

    public sealed class RuleDef
    {
        public string Type { get; }
        public string? Field { get; }
        public string? Value { get; }
        public bool Enabled { get; }
        public RuleDef(string type, string? field, string? value, bool enabled = true)
        { Type = type; Field = field; Value = value; Enabled = enabled; }
    }
}