using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform.Transforms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform.Transforms;

[TestClass]
public class TagUtilitiesTests
{
    [TestMethod]
    public void AppendTag_WhenExistingTagsEmpty_ReturnsNewTag()
    {
        var result = TagUtilities.AppendTag(null, "High");
        Assert.AreEqual("High", result);
    }

    [TestMethod]
    public void AppendTag_WhenExistingTagsPresent_AppendsWithSeparator()
    {
        var result = TagUtilities.AppendTag("Bug", "High");
        Assert.AreEqual("Bug; High", result);
    }

    [TestMethod]
    public void Deduplicate_RemovesDuplicatesCaseInsensitively()
    {
        var result = TagUtilities.Deduplicate("High; high; MEDIUM");
        // "high" and "MEDIUM" are separate case-insensitive groups; only first occurrence kept
        Assert.AreEqual("High; MEDIUM", result);
    }

    [TestMethod]
    public void Deduplicate_PreservesOriginalCase()
    {
        var result = TagUtilities.Deduplicate("BUG; bug; Bug");
        Assert.AreEqual("BUG", result);
    }

    [TestMethod]
    public void Deduplicate_HandlesEmptyInput()
    {
        Assert.AreEqual(string.Empty, TagUtilities.Deduplicate(null));
        Assert.AreEqual(string.Empty, TagUtilities.Deduplicate(string.Empty));
        Assert.AreEqual(string.Empty, TagUtilities.Deduplicate("   "));
    }
}
