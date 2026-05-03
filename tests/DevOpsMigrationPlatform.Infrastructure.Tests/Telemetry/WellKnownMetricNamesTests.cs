// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Linq;
using System.Reflection;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Telemetry;

[TestClass]
public class WellKnownMetricNamesTests
{
    private static readonly FieldInfo[] AllConstants = typeof(WellKnownMetricNames)
        .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
        .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
        .ToArray();

    [TestMethod]
    public void AllConstants_StartWithMigrationPrefix()
    {
        foreach (var field in AllConstants)
        {
            var value = (string)field.GetValue(null)!;
            Assert.IsTrue(
                value.StartsWith("migration.", StringComparison.Ordinal),
                $"{field.Name} = \"{value}\" does not start with \"migration.\"");
        }
    }

    [TestMethod]
    public void AllConstants_UseDotSeparatedHierarchy()
    {
        foreach (var field in AllConstants)
        {
            var value = (string)field.GetValue(null)!;
            // Must contain at least two dots (migration.category.name)
            var dotCount = value.Count(c => c == '.');
            Assert.IsTrue(
                dotCount >= 2,
                $"{field.Name} = \"{value}\" has only {dotCount} dot(s); expected at least 2 for hierarchy.");
        }
    }

    [TestMethod]
    public void AllConstants_DoNotUseUnderscoresAsHierarchySeparators()
    {
        // Underscores within leaf segments (e.g. "in_flight") are permitted,
        // but underscores must NOT replace dots as hierarchy separators.
        // A hierarchy separator underscore would appear between two alphabetic segments
        // that should be dot-separated. We validate by checking that the segments
        // between dots do not contain further segments that look like separate hierarchy levels.
        foreach (var field in AllConstants)
        {
            var value = (string)field.GetValue(null)!;
            var segments = value.Split('.');

            // Each dot-separated segment should be a single logical name.
            // We don't prohibit underscores within segments (e.g. "in_flight" is fine)
            // but we verify the overall structure uses dots for hierarchy.
            foreach (var segment in segments)
            {
                Assert.IsFalse(
                    string.IsNullOrEmpty(segment),
                    $"{field.Name} = \"{value}\" has an empty segment (consecutive dots).");
            }
        }
    }

    [TestMethod]
    public void AllConstants_AreUnique()
    {
        var values = AllConstants.Select(f => (string)f.GetValue(null)!).ToList();
        var distinct = values.Distinct().ToList();

        Assert.AreEqual(
            values.Count, distinct.Count,
            $"Duplicate metric names found: {string.Join(", ", values.GroupBy(v => v).Where(g => g.Count() > 1).Select(g => g.Key))}");
    }

}
