using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Models;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Services;

[TestClass]
public class WorkItemFieldFilterEvaluatorTests
{
    [TestMethod]
    public void PassesFilters_NullFilters_ReturnsTrue()
    {
        var item = MakeItem(("System.WorkItemType", "Bug"));
        Assert.IsTrue(AzureDevOpsWorkItemFetchService.PassesFilters(item, null));
    }

    [TestMethod]
    public void PassesFilters_EmptyFilters_ReturnsTrue()
    {
        var item = MakeItem(("System.WorkItemType", "Bug"));
        Assert.IsTrue(AzureDevOpsWorkItemFetchService.PassesFilters(item, new List<WorkItemFieldFilterOptions>()));
    }

    [TestMethod]
    public void PassesFilters_Equals_CaseInsensitive_ReturnsTrue()
    {
        var item = MakeItem(("System.WorkItemType", "Bug"));
        var filters = new List<WorkItemFieldFilterOptions>
        {
            new("System.WorkItemType", FilterOperator.Equals, "bug")
        };
        Assert.IsTrue(AzureDevOpsWorkItemFetchService.PassesFilters(item, filters));
    }

    [TestMethod]
    public void PassesFilters_Equals_Mismatch_ReturnsFalse()
    {
        var item = MakeItem(("System.WorkItemType", "Task"));
        var filters = new List<WorkItemFieldFilterOptions>
        {
            new("System.WorkItemType", FilterOperator.Equals, "Bug")
        };
        Assert.IsFalse(AzureDevOpsWorkItemFetchService.PassesFilters(item, filters));
    }

    [TestMethod]
    public void PassesFilters_NotEquals_ReturnsTrue()
    {
        var item = MakeItem(("System.WorkItemType", "Task"));
        var filters = new List<WorkItemFieldFilterOptions>
        {
            new("System.WorkItemType", FilterOperator.NotEquals, "Bug")
        };
        Assert.IsTrue(AzureDevOpsWorkItemFetchService.PassesFilters(item, filters));
    }

    [TestMethod]
    public void PassesFilters_NotEquals_SameValue_ReturnsFalse()
    {
        var item = MakeItem(("System.WorkItemType", "Bug"));
        var filters = new List<WorkItemFieldFilterOptions>
        {
            new("System.WorkItemType", FilterOperator.NotEquals, "Bug")
        };
        Assert.IsFalse(AzureDevOpsWorkItemFetchService.PassesFilters(item, filters));
    }

    [TestMethod]
    public void PassesFilters_Contains_SubstringMatch_ReturnsTrue()
    {
        var item = MakeItem(("System.Title", "Fix the login page bug"));
        var filters = new List<WorkItemFieldFilterOptions>
        {
            new("System.Title", FilterOperator.Contains, "login page")
        };
        Assert.IsTrue(AzureDevOpsWorkItemFetchService.PassesFilters(item, filters));
    }

    [TestMethod]
    public void PassesFilters_Contains_NoMatch_ReturnsFalse()
    {
        var item = MakeItem(("System.Title", "Fix the header"));
        var filters = new List<WorkItemFieldFilterOptions>
        {
            new("System.Title", FilterOperator.Contains, "login page")
        };
        Assert.IsFalse(AzureDevOpsWorkItemFetchService.PassesFilters(item, filters));
    }

    [TestMethod]
    public void PassesFilters_AndSemantics_AllMustMatch()
    {
        var item = MakeItem(
            ("System.WorkItemType", "Bug"),
            ("System.State", "Active"));
        var filters = new List<WorkItemFieldFilterOptions>
        {
            new("System.WorkItemType", FilterOperator.Equals, "Bug"),
            new("System.State", FilterOperator.Equals, "Active")
        };
        Assert.IsTrue(AzureDevOpsWorkItemFetchService.PassesFilters(item, filters));
    }

    [TestMethod]
    public void PassesFilters_AndSemantics_OneFailsReturnsFalse()
    {
        var item = MakeItem(
            ("System.WorkItemType", "Bug"),
            ("System.State", "Closed"));
        var filters = new List<WorkItemFieldFilterOptions>
        {
            new("System.WorkItemType", FilterOperator.Equals, "Bug"),
            new("System.State", FilterOperator.Equals, "Active")
        };
        Assert.IsFalse(AzureDevOpsWorkItemFetchService.PassesFilters(item, filters));
    }

    [TestMethod]
    public void PassesFilters_MissingField_Equals_ReturnsFalse()
    {
        var item = MakeItem(("System.State", "Active"));
        var filters = new List<WorkItemFieldFilterOptions>
        {
            new("System.WorkItemType", FilterOperator.Equals, "Bug")
        };
        Assert.IsFalse(AzureDevOpsWorkItemFetchService.PassesFilters(item, filters));
    }

    [TestMethod]
    public void PassesFilters_MissingField_NotEquals_ReturnsTrue()
    {
        var item = MakeItem(("System.State", "Active"));
        var filters = new List<WorkItemFieldFilterOptions>
        {
            new("System.WorkItemType", FilterOperator.NotEquals, "Bug")
        };
        Assert.IsTrue(AzureDevOpsWorkItemFetchService.PassesFilters(item, filters));
    }

    [TestMethod]
    public void PassesFilters_NullFilterValue_Equals_NullField_ReturnsTrue()
    {
        var fields = new Dictionary<string, object?> { ["System.Description"] = null };
        var item = new FetchedWorkItem(1, fields);
        var filters = new List<WorkItemFieldFilterOptions>
        {
            new("System.Description", FilterOperator.Equals, null)
        };
        Assert.IsTrue(AzureDevOpsWorkItemFetchService.PassesFilters(item, filters));
    }

    [TestMethod]
    public void PassesFilters_NullFilterValue_Equals_NonNullField_ReturnsFalse()
    {
        var item = MakeItem(("System.Description", "some text"));
        var filters = new List<WorkItemFieldFilterOptions>
        {
            new("System.Description", FilterOperator.Equals, null)
        };
        Assert.IsFalse(AzureDevOpsWorkItemFetchService.PassesFilters(item, filters));
    }

    // --- Regex / NotRegex ---

    [TestMethod]
    public void PassesFilters_Regex_MatchingPattern_ReturnsTrue()
    {
        var item = MakeItem(("System.WorkItemType", "Bug"));
        var filters = new List<WorkItemFieldFilterOptions>
        {
            new("System.WorkItemType", FilterOperator.Regex, "^Bug$")
        };
        Assert.IsTrue(AzureDevOpsWorkItemFetchService.PassesFilters(item, filters));
    }

    [TestMethod]
    public void PassesFilters_Regex_CaseInsensitive_ReturnsTrue()
    {
        var item = MakeItem(("System.WorkItemType", "BUG"));
        var filters = new List<WorkItemFieldFilterOptions>
        {
            new("System.WorkItemType", FilterOperator.Regex, "^bug$")
        };
        Assert.IsTrue(AzureDevOpsWorkItemFetchService.PassesFilters(item, filters));
    }

    [TestMethod]
    public void PassesFilters_Regex_NonMatchingPattern_ReturnsFalse()
    {
        var item = MakeItem(("System.WorkItemType", "Task"));
        var filters = new List<WorkItemFieldFilterOptions>
        {
            new("System.WorkItemType", FilterOperator.Regex, "^Bug$")
        };
        Assert.IsFalse(AzureDevOpsWorkItemFetchService.PassesFilters(item, filters));
    }

    [TestMethod]
    public void PassesFilters_Regex_PartialPatternMatch_ReturnsTrue()
    {
        var item = MakeItem(("System.Title", "Fix login page bug"));
        var filters = new List<WorkItemFieldFilterOptions>
        {
            new("System.Title", FilterOperator.Regex, "login.*bug")
        };
        Assert.IsTrue(AzureDevOpsWorkItemFetchService.PassesFilters(item, filters));
    }

    [TestMethod]
    public void PassesFilters_Regex_MissingField_ReturnsFalse()
    {
        var item = MakeItem(("System.State", "Active"));
        var filters = new List<WorkItemFieldFilterOptions>
        {
            new("System.WorkItemType", FilterOperator.Regex, "^Bug$")
        };
        Assert.IsFalse(AzureDevOpsWorkItemFetchService.PassesFilters(item, filters));
    }

    [TestMethod]
    public void PassesFilters_NotRegex_NonMatchingPattern_ReturnsTrue()
    {
        var item = MakeItem(("System.WorkItemType", "Task"));
        var filters = new List<WorkItemFieldFilterOptions>
        {
            new("System.WorkItemType", FilterOperator.NotRegex, "^Bug$")
        };
        Assert.IsTrue(AzureDevOpsWorkItemFetchService.PassesFilters(item, filters));
    }

    [TestMethod]
    public void PassesFilters_NotRegex_MatchingPattern_ReturnsFalse()
    {
        var item = MakeItem(("System.WorkItemType", "Bug"));
        var filters = new List<WorkItemFieldFilterOptions>
        {
            new("System.WorkItemType", FilterOperator.NotRegex, "^Bug$")
        };
        Assert.IsFalse(AzureDevOpsWorkItemFetchService.PassesFilters(item, filters));
    }

    [TestMethod]
    public void PassesFilters_NotRegex_MissingField_ReturnsTrue()
    {
        var item = MakeItem(("System.State", "Active"));
        var filters = new List<WorkItemFieldFilterOptions>
        {
            new("System.WorkItemType", FilterOperator.NotRegex, "^Bug$")
        };
        // Absent field = does not match = passes NotRegex
        Assert.IsTrue(AzureDevOpsWorkItemFetchService.PassesFilters(item, filters));
    }

    [TestMethod]
    public void PassesFilters_IncludeAndExclude_BothPass_ReturnsTrue()
    {
        var item = MakeItem(
            ("System.WorkItemType", "Bug"),
            ("System.State", "Active"));
        var filters = new List<WorkItemFieldFilterOptions>
        {
            new("System.WorkItemType", FilterOperator.Regex, "^Bug$"),     // include
            new("System.State", FilterOperator.NotRegex, "^Closed$")       // exclude closed items
        };
        Assert.IsTrue(AzureDevOpsWorkItemFetchService.PassesFilters(item, filters));
    }

    [TestMethod]
    public void PassesFilters_IncludeAndExclude_ExcludeHits_ReturnsFalse()
    {
        var item = MakeItem(
            ("System.WorkItemType", "Bug"),
            ("System.State", "Closed"));
        var filters = new List<WorkItemFieldFilterOptions>
        {
            new("System.WorkItemType", FilterOperator.Regex, "^Bug$"),     // include
            new("System.State", FilterOperator.NotRegex, "^Closed$")       // exclude closed items
        };
        Assert.IsFalse(AzureDevOpsWorkItemFetchService.PassesFilters(item, filters));
    }

    private static FetchedWorkItem MakeItem(params (string Key, object? Value)[] fields)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var (key, value) in fields)
            dict[key] = value;
        return new FetchedWorkItem(1, dict);
    }
}
