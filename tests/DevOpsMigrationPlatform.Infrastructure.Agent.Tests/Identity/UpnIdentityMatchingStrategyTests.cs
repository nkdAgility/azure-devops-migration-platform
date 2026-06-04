// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Identity;
using DevOpsMigrationPlatform.Infrastructure.Agent.Identity.Strategies;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Identity;

[TestClass]
public sealed class UpnIdentityMatchingStrategyTests
{
    private static readonly UpnIdentityMatchingStrategy Strategy = new();

    private static IReadOnlyList<IdentityCandidate> Candidates(params IdentityCandidate[] items) => items;

    [TestMethod]
    public void Match_ExactUpn_CaseInsensitive_ReturnsSingleMatch()
    {
        var candidates = Candidates(
            new IdentityCandidate("desc-bob", "BOB@target.com", "Bob Smith"),
            new IdentityCandidate("desc-alice", "alice@target.com", "Alice Jones"));

        var result = Strategy.Match("bob@target.com", "Bob Smith", candidates);

        Assert.IsTrue(result.IsMatch);
        Assert.AreEqual("desc-bob", result.Descriptor);
        Assert.AreEqual(1, result.MatchCount);
    }

    [TestMethod]
    public void Match_NoUpnMatch_ReturnsNone()
    {
        var candidates = Candidates(new IdentityCandidate("desc-alice", "alice@target.com", "Alice Jones"));

        var result = Strategy.Match("nobody@target.com", "Nobody", candidates);

        Assert.IsFalse(result.IsMatch);
        Assert.IsFalse(result.IsAmbiguous);
        Assert.IsNull(result.Descriptor);
        Assert.AreEqual(0, result.MatchCount);
    }

    [TestMethod]
    public void Match_MultipleCandidates_OneUpnMatch_ReturnsThatMatch()
    {
        var candidates = Candidates(
            new IdentityCandidate("desc-a", "a@target.com", "Person A"),
            new IdentityCandidate("desc-b", "b@target.com", "Person B"),
            new IdentityCandidate("desc-c", null, "Person C"));

        var result = Strategy.Match("b@target.com", "Person B", candidates);

        Assert.IsTrue(result.IsMatch);
        Assert.AreEqual("desc-b", result.Descriptor);
    }

    [TestMethod]
    public void Match_DuplicateUpn_ReturnsAmbiguous()
    {
        var candidates = Candidates(
            new IdentityCandidate("desc-1", "dup@target.com", "Dup One"),
            new IdentityCandidate("desc-2", "dup@target.com", "Dup Two"));

        var result = Strategy.Match("dup@target.com", "Dup One", candidates);

        Assert.IsTrue(result.IsAmbiguous);
        Assert.IsFalse(result.IsMatch);
        Assert.IsNull(result.Descriptor);
        Assert.AreEqual(2, result.MatchCount);
    }

    [TestMethod]
    public void Match_EmptySourceUpn_ReturnsNone()
    {
        var candidates = Candidates(new IdentityCandidate("desc-a", "a@target.com", "Person A"));

        Assert.IsFalse(Strategy.Match("", "Person A", candidates).IsMatch);
        Assert.IsFalse(Strategy.Match("   ", "Person A", candidates).IsMatch);
    }
}
