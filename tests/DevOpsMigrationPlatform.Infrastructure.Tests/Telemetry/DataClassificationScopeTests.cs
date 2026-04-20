using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Telemetry;

[TestClass]
public class DataClassificationScopeTests
{
    [TestMethod]
    public void Begin_SetsCurrentToClassification()
    {
        using (DataClassificationScope.Begin(DataClassification.Customer))
        {
            Assert.AreEqual(DataClassification.Customer, DataClassificationScope.Current);
        }
    }

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

    [TestMethod]
    public void DataClassificationState_HasCorrectKeyAndValue()
    {
        var state = new DataClassificationState(DataClassification.Customer);

        Assert.AreEqual(1, state.Count);
        Assert.AreEqual(DataClassificationScope.ScopeKey, state[0].Key);
        Assert.AreEqual("Customer", state[0].Value);
    }

    [TestMethod]
    public void DataClassificationState_IsEnumerable()
    {
        var state = new DataClassificationState(DataClassification.Derived);
        var items = new List<KeyValuePair<string, object>>();

        foreach (var item in state)
        {
            items.Add(item);
        }

        Assert.AreEqual(1, items.Count);
        Assert.AreEqual("Derived", items[0].Value);
    }

    [TestMethod]
    public void DataClassificationState_ToString_ContainsKeyAndValue()
    {
        var state = new DataClassificationState(DataClassification.System);
        var str = state.ToString();

        Assert.IsTrue(str.Contains("DataClassification"));
        Assert.IsTrue(str.Contains("System"));
    }
}
