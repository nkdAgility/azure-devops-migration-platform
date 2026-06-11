// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform;

[TestClass]
public class FieldTransformFactoryTests
{
    private FieldTransformFactory _factory = null!;

    [TestInitialize]
    public void Setup() => _factory = new FieldTransformFactory();

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Create_WithUnknownType_ThrowsInvalidOperationException()
    {
        var options = new FieldTransformRuleOptions { Type = "UnknownType" };

        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => _factory.Create(options, "Group1", 1));

        StringAssert.Contains(ex.Message, "UnknownType");
        StringAssert.Contains(ex.Message, "Supported types:");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Create_WithEmptyType_ThrowsInvalidOperationException()
    {
        var options = new FieldTransformRuleOptions { Type = string.Empty };

        Assert.ThrowsExactly<InvalidOperationException>(
            () => _factory.Create(options, "Group1", 1));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Create_WhenNameIsNull_GeneratesDefaultName()
    {
        var mockTransform = new Mock<IFieldTransform>(MockBehavior.Strict);
        mockTransform.SetupGet(t => t.Name).Returns("Group1.SetField1");
        mockTransform.SetupGet(t => t.Type).Returns("SetField");

        _factory.Register("SetField", (opts, group, ord) =>
        {
            Assert.IsNull(opts.Name);
            Assert.AreEqual("Group1", group);
            Assert.AreEqual(1, ord);
            return mockTransform.Object;
        });

        var options = new FieldTransformRuleOptions { Type = "SetField", Name = null };
        var result = _factory.Create(options, "Group1", 1);

        Assert.IsNotNull(result);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Create_WhenNameIsProvided_UsesProvidedName()
    {
        var mockTransform = new Mock<IFieldTransform>(MockBehavior.Strict);
        mockTransform.SetupGet(t => t.Name).Returns("MyCustomName");
        mockTransform.SetupGet(t => t.Type).Returns("SetField");

        string? capturedName = null;
        _factory.Register("SetField", (opts, group, ord) =>
        {
            capturedName = opts.Name;
            return mockTransform.Object;
        });

        var options = new FieldTransformRuleOptions { Type = "SetField", Name = "MyCustomName" };
        _factory.Create(options, "Group1", 1);

        Assert.AreEqual("MyCustomName", capturedName);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Create_WithIdentityFieldAsField_ThrowsInvalidOperationException()
    {
        var options = new FieldTransformRuleOptions { Type = "SetField", Field = "System.CreatedBy" };

        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => _factory.Create(options, "Group1", 1));

        StringAssert.Contains(ex.Message, "System.CreatedBy");
        StringAssert.Contains(ex.Message, "identity field");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Create_WithIdentityFieldAsTargetField_ThrowsInvalidOperationException()
    {
        var options = new FieldTransformRuleOptions { Type = "CopyField", TargetField = "System.ChangedBy" };

        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => _factory.Create(options, "Group1", 1));

        StringAssert.Contains(ex.Message, "System.ChangedBy");
        StringAssert.Contains(ex.Message, "identity field");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Create_WithRegisteredType_CreatesTransform()
    {
        var mockTransform = new Mock<IFieldTransform>(MockBehavior.Strict);
        mockTransform.SetupGet(t => t.Type).Returns("SetField");
        mockTransform.SetupGet(t => t.Name).Returns("MyGroup.SetField1");

        _factory.Register("SetField", (opts, group, ord) => mockTransform.Object);

        var options = new FieldTransformRuleOptions { Type = "SetField" };
        var result = _factory.Create(options, "MyGroup", 1);

        Assert.AreSame(mockTransform.Object, result);
    }
}
