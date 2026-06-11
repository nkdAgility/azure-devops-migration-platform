// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Telemetry;

[TestClass]
public class DataClassificationScopeTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Begin_SetsCurrentToClassification()
    {
        using (DataClassificationScope.Begin(DataClassification.Customer))
        {
            Assert.AreEqual(DataClassification.Customer, DataClassificationScope.Current);
        }
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Begin_RestoresPreviousOnDispose()
    {
        Assert.IsNull(DataClassificationScope.Current);

        using (DataClassificationScope.Begin(DataClassification.Customer))
        {
            Assert.AreEqual(DataClassification.Customer, DataClassificationScope.Current);
        }

        Assert.IsNull(DataClassificationScope.Current);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Begin_NestedScopes_InnermostWins()
    {
        using (DataClassificationScope.Begin(DataClassification.System))
        {
            Assert.AreEqual(DataClassification.System, DataClassificationScope.Current);

            using (DataClassificationScope.Begin(DataClassification.Customer))
            {
                Assert.AreEqual(DataClassification.Customer, DataClassificationScope.Current);
            }

            Assert.AreEqual(DataClassification.System, DataClassificationScope.Current);
        }

        Assert.IsNull(DataClassificationScope.Current);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void DataClassificationState_HasCorrectLoggingScopeShape()
    {
        var state = new DataClassificationState(DataClassification.Customer);

        // Must expose exactly one key-value pair for ILogger.BeginScope to work
        Assert.AreEqual(1, state.Count);
        var pair = state[0];
        Assert.AreEqual(DataClassificationScope.ScopeKey, pair.Key);
        Assert.AreEqual(DataClassification.Customer.ToString(), pair.Value);

        // Must be enumerable (IReadOnlyList contract)
        var items = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, object>>();
        foreach (var item in state) items.Add(item);
        Assert.AreEqual(1, items.Count);
    }
}
