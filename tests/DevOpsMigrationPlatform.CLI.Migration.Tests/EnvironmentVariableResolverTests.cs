using DevOpsMigrationPlatform.CLI.Migration.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests;

[TestClass]
public class EnvironmentVariableResolverTests
{
    private const string TestVarName = "DEVOPS_MIGRATION_TEST_VAR_1234";

    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable(TestVarName, null);
    }

    [TestMethod]
    public void Resolve_PlainValue_ReturnsSameValue()
    {
        var result = EnvironmentVariableResolver.Resolve("hello", "field");
        Assert.AreEqual("hello", result);
    }

    [TestMethod]
    public void Resolve_NullValue_ReturnsEmpty()
    {
        var result = EnvironmentVariableResolver.Resolve(null, "field");
        Assert.AreEqual(string.Empty, result);
    }

    [TestMethod]
    public void Resolve_EmptyValue_ReturnsEmpty()
    {
        var result = EnvironmentVariableResolver.Resolve("", "field");
        Assert.AreEqual(string.Empty, result);
    }

    [TestMethod]
    public void Resolve_EnvVarSet_ReturnsResolvedValue()
    {
        Environment.SetEnvironmentVariable(TestVarName, "resolved-value");

        var result = EnvironmentVariableResolver.Resolve($"$ENV:{TestVarName}", "myField");

        Assert.AreEqual("resolved-value", result);
    }

    [TestMethod]
    public void Resolve_EnvVarNotSet_ThrowsInvalidOperation()
    {
        Environment.SetEnvironmentVariable(TestVarName, null);

        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => EnvironmentVariableResolver.Resolve($"$ENV:{TestVarName}", "myField"));

        Assert.IsTrue(ex.Message.Contains(TestVarName));
        Assert.IsTrue(ex.Message.Contains("myField"));
    }

    [TestMethod]
    public void Resolve_EmptyEnvRef_ThrowsInvalidOperation()
    {
        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => EnvironmentVariableResolver.Resolve("$ENV:", "myField"));

        Assert.IsTrue(ex.Message.Contains("empty"));
    }

    [TestMethod]
    public void IsEnvReference_WithPrefix_ReturnsTrue()
    {
        Assert.IsTrue(EnvironmentVariableResolver.IsEnvReference("$ENV:MY_VAR"));
    }

    [TestMethod]
    public void IsEnvReference_WithoutPrefix_ReturnsFalse()
    {
        Assert.IsFalse(EnvironmentVariableResolver.IsEnvReference("plain-value"));
    }

    [TestMethod]
    public void IsEnvReference_Null_ReturnsFalse()
    {
        Assert.IsFalse(EnvironmentVariableResolver.IsEnvReference(null));
    }
}
