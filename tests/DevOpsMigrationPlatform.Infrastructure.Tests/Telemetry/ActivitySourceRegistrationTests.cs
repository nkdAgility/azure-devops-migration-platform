// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) NKD Agility Limited

#if !NETFRAMEWORK
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Telemetry;

[TestClass]
public class ActivitySourceRegistrationTests
{
    /// <summary>
    /// All WellKnownActivitySourceNames constants must correspond to an ActivitySource
    /// that is registered in ServiceDefaults. This test verifies the constants exist and
    /// that every source used in Infrastructure assemblies references a well-known name.
    /// </summary>
    [TestMethod]
    public void AllWellKnownSources_AreDeclaredAsConstants()
    {
        var constants = typeof(WellKnownActivitySourceNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .Select(f => (string)f.GetValue(null)!)
            .ToHashSet();

        // Must contain at least the 3 core sources
        Assert.IsTrue(constants.Contains(WellKnownActivitySourceNames.Migration),
            "Missing Migration activity source constant.");
        Assert.IsTrue(constants.Contains(WellKnownActivitySourceNames.Discovery),
            "Missing Discovery activity source constant.");
        Assert.IsTrue(constants.Contains(WellKnownActivitySourceNames.ControlPlane),
            "Missing ControlPlane activity source constant.");
    }

    [TestMethod]
    public void ActivitySource_Migration_CanStartActivity()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == WellKnownActivitySourceNames.Migration,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        using var source = new ActivitySource(WellKnownActivitySourceNames.Migration);
        using var activity = source.StartActivity("test.operation");

        Assert.IsNotNull(activity, "ActivitySource with Migration name should create activities when listener is registered.");
        Assert.AreEqual("test.operation", activity.OperationName);
    }

    [TestMethod]
    public void ActivitySource_Discovery_CanStartActivity()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == WellKnownActivitySourceNames.Discovery,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        using var source = new ActivitySource(WellKnownActivitySourceNames.Discovery);
        using var activity = source.StartActivity("test.discovery");

        Assert.IsNotNull(activity);
        Assert.AreEqual("test.discovery", activity.OperationName);
    }

    [TestMethod]
    public void ActivitySource_ControlPlane_CanStartActivity()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == WellKnownActivitySourceNames.ControlPlane,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        using var source = new ActivitySource(WellKnownActivitySourceNames.ControlPlane);
        using var activity = source.StartActivity("test.controlplane");

        Assert.IsNotNull(activity);
        Assert.AreEqual("test.controlplane", activity.OperationName);
    }

    [TestMethod]
    public void ActivityTags_CanCarryJobIdAndModule()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == WellKnownActivitySourceNames.Migration,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        using var source = new ActivitySource(WellKnownActivitySourceNames.Migration);
        using var activity = source.StartActivity("workitems.export");
        Assert.IsNotNull(activity);

        activity.SetTag("job.id", "job-123");
        activity.SetTag("module", "workitems");

        var jobId = activity.Tags.FirstOrDefault(t => t.Key == "job.id");
        var module = activity.Tags.FirstOrDefault(t => t.Key == "module");

        Assert.AreEqual("job-123", jobId.Value);
        Assert.AreEqual("workitems", module.Value);
    }
}
#endif
