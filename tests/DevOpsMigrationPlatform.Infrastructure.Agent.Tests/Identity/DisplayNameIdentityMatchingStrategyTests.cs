// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Identity;
using DevOpsMigrationPlatform.Infrastructure.Agent.Identity.Strategies;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Identity;

[TestClass]
public sealed class DisplayNameIdentityMatchingStrategyTests
{
    private static readonly DisplayNameIdentityMatchingStrategy Strategy = new();

    private static IReadOnlyList<IdentityCandidate> Candidates(params IdentityCandidate[] items) => items;

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Match_SingleDisplayName_CaseInsensitive_ReturnsMatch()
    {
        var candidates = Candidates(
            new IdentityCandidate("desc-bob", "bob@target.com", "Bob Smith"),
            new IdentityCandidate("desc-alice", "alice@target.com", "Alice Jones"));

        var result = Strategy.Match("noupn@source.com", "bob smith", candidates);

        Assert.IsTrue(result.IsMatch);
        Assert.AreEqual("desc-bob", result.Descriptor);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Match_WhitespaceAndUnicodeNfc_AreNormalised()
    {
        // Candidate uses precomposed "é" (NFC); source uses decomposed "e" + combining accent (NFD) with padding.
        var candidates = Candidates(new IdentityCandidate("desc-rene", "rene@target.com", "René Dupont"));

        var result = Strategy.Match("noupn@source.com", "  René Dupont  ", candidates);

        Assert.IsTrue(result.IsMatch);
        Assert.AreEqual("desc-rene", result.Descriptor);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Match_AmbiguousDisplayName_ReturnsAmbiguousWithCount()
    {
        var candidates = Candidates(
            new IdentityCandidate("desc-1", "j1@target.com", "John Smith"),
            new IdentityCandidate("desc-2", "j2@target.com", "John Smith"),
            new IdentityCandidate("desc-3", "j3@target.com", "Jane Doe"));

        var result = Strategy.Match("noupn@source.com", "John Smith", candidates);

        Assert.IsTrue(result.IsAmbiguous);
        Assert.IsFalse(result.IsMatch);
        Assert.IsNull(result.Descriptor);
        Assert.AreEqual(2, result.MatchCount);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Match_NoDisplayNameMatch_ReturnsNone()
    {
        var candidates = Candidates(new IdentityCandidate("desc-1", "j1@target.com", "John Smith"));

        Assert.AreEqual(0, Strategy.Match("noupn@source.com", "Nobody Here", candidates).MatchCount);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Match_EmptySourceDisplayName_ReturnsNone()
    {
        var candidates = Candidates(new IdentityCandidate("desc-1", "j1@target.com", "John Smith"));

        Assert.IsFalse(Strategy.Match("x@source.com", "  ", candidates).IsMatch);
    }
}
