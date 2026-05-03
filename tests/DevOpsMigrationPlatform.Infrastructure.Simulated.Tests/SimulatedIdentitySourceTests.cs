// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Identity;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Simulated;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Tests;

[TestClass]
public class SimulatedIdentitySourceTests
{
    [TestMethod]
    public async Task EnumerateIdentitiesAsync_ReturnsDeterministicIdentities()
    {
        // Arrange
        var source = new SimulatedIdentitySource();

        // Act — enumerate twice to verify determinism
        var firstRun = await CollectAsync(source, "TestProject");
        var secondRun = await CollectAsync(source, "TestProject");

        // Assert
        Assert.AreEqual(firstRun.Count, secondRun.Count, "Same number on every run");
        for (var i = 0; i < firstRun.Count; i++)
            Assert.AreEqual(firstRun[i].Descriptor, secondRun[i].Descriptor, $"Descriptor mismatch at index {i}");
    }

    [TestMethod]
    public async Task EnumerateIdentitiesAsync_AllHaveRequiredFields()
    {
        // Arrange
        var source = new SimulatedIdentitySource();

        // Act
        var identities = await CollectAsync(source, "TestProject");

        // Assert
        Assert.IsTrue(identities.Count > 0, "Should produce at least one identity");
        foreach (var id in identities)
        {
            Assert.IsFalse(string.IsNullOrEmpty(id.Descriptor), $"Descriptor should not be empty for {id.UniqueName}");
            Assert.IsFalse(string.IsNullOrEmpty(id.DisplayName), $"DisplayName should not be empty for {id.UniqueName}");
            Assert.IsFalse(string.IsNullOrEmpty(id.UniqueName), $"UniqueName should not be empty");
            Assert.IsFalse(string.IsNullOrEmpty(id.SourceType), $"SourceType should not be empty for {id.UniqueName}");
        }
    }

    [TestMethod]
    public async Task EnumerateIdentitiesAsync_ContainsBothUsersAndGroups()
    {
        // Arrange
        var source = new SimulatedIdentitySource();

        // Act
        var identities = await CollectAsync(source, "AnyProject");

        // Assert
        var hasUsers = identities.Exists(id => id.SourceType == "User");
        var hasGroups = identities.Exists(id => id.SourceType == "Group");

        Assert.IsTrue(hasUsers, "Should contain at least one user identity");
        Assert.IsTrue(hasGroups, "Should contain at least one group identity");
    }

    [TestMethod]
    // TODO: [test-validity] Score 14/25 — Only verifies count equality across two project names.
    // The Simulated source is project-agnostic by design; this assertion rarely fails except by
    // intentional change. Rewrite to compare descriptor values (not just count) between runs,
    // or replace with an XML doc on SimulatedIdentitySource noting project-agnostic behaviour.
    public async Task EnumerateIdentitiesAsync_IgnoresProjectName_ReturnsSameSet()
    {
        // Arrange — verifies project name doesn't affect the result (simulated is project-agnostic)
        var source = new SimulatedIdentitySource();

        var project1 = await CollectAsync(source, "ProjectA");
        var project2 = await CollectAsync(source, "ProjectB");

        Assert.AreEqual(project1.Count, project2.Count);
    }

    private static async Task<List<IdentityDescriptor>> CollectAsync(
        SimulatedIdentitySource source, string project)
    {
        var result = new List<IdentityDescriptor>();
        await foreach (var id in source.EnumerateIdentitiesAsync(project, CancellationToken.None))
            result.Add(id);
        return result;
    }
}
