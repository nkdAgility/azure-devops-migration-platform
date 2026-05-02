// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) NKD Agility Limited

using System;
using System.Linq;
using System.Reflection;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Telemetry;

[TestClass]
public class WellKnownActivitySourceNamesTests
{
    private static readonly FieldInfo[] AllConstants = typeof(WellKnownActivitySourceNames)
        .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
        .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
        .ToArray();

    [TestMethod]
    public void AllConstants_StartWithPlatformPrefix()
    {
        foreach (var field in AllConstants)
        {
            var value = (string)field.GetValue(null)!;
            Assert.IsTrue(
                value.StartsWith("DevOpsMigrationPlatform.", StringComparison.Ordinal),
                $"{field.Name} = \"{value}\" does not start with \"DevOpsMigrationPlatform.\"");
        }
    }

    [TestMethod]
    public void AllConstants_AreUnique()
    {
        var values = AllConstants.Select(f => (string)f.GetValue(null)!).ToList();
        var distinct = values.Distinct().ToList();

        Assert.AreEqual(
            values.Count, distinct.Count,
            $"Duplicate activity source names found: {string.Join(", ", values.GroupBy(v => v).Where(g => g.Count() > 1).Select(g => g.Key))}");
    }


}
