// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Nodes;

/// <summary>
/// Reads a TFS classification tree (#169). <c>ICommonStructureService.ListStructures</c> returns
/// only the two root structures, typed <c>ProjectModelHierarchy</c> (areas) and
/// <c>ProjectLifecycle</c> (iterations); the full tree comes from <c>GetNodesXml</c>. Paths are
/// written in the package format the REST connector uses: no leading backslash.
/// </summary>
public static class ClassificationNodesXml
{
    /// <summary>True for the root structure that holds area nodes.</summary>
    public static bool IsAreaRoot(string? structureType) =>
        string.Equals(structureType, "ProjectModelHierarchy", StringComparison.OrdinalIgnoreCase);

    /// <summary>True for the root structure that holds iteration nodes.</summary>
    public static bool IsIterationRoot(string? structureType) =>
        string.Equals(structureType, "ProjectLifecycle", StringComparison.OrdinalIgnoreCase);

    /// <summary>Every node in the tree, root first, depth first, with its iteration dates if any.</summary>
    public static IEnumerable<IterationNodeEntry> Flatten(XmlElement nodesXml)
    {
        if (nodesXml is null) throw new ArgumentNullException(nameof(nodesXml));

        var nodes = nodesXml.SelectNodes("descendant-or-self::Node");
        if (nodes is null) yield break;

        foreach (XmlElement node in nodes)
        {
            var path = node.GetAttribute("Path").TrimStart('\\');
            if (path.Length == 0) continue;

            yield return new IterationNodeEntry(path, ReadDate(node, "StartDate"), ReadDate(node, "FinishDate"), false);
        }
    }

    private static DateTimeOffset? ReadDate(XmlElement node, string attribute)
    {
        var value = node.GetAttribute(attribute);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date)
            && date.UtcDateTime != DateTime.MinValue
            ? date
            : null;
    }
}
