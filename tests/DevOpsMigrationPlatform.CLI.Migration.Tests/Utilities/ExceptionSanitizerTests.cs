// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DevOpsMigrationPlatform.CLI.Migration.Configuration;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Utilities;

[TestClass]
[TestCategory("UnitTest")]
[ExcludeFromCodeCoverage]
public class ExceptionSanitizerTests
{
    [TestMethod]
    public void SanitizeMessage_WithNullMessage_ReturnsEmpty()
    {
        // Arrange
        var message = (string?)null;

        // Act
        var result = ExceptionSanitizer.SanitizeMessage(message);

        // Assert
        Assert.AreEqual(string.Empty, result);
    }

    [TestMethod]
    public void SanitizeMessage_WithEmptyMessage_ReturnsEmpty()
    {
        // Arrange
        var message = string.Empty;

        // Act
        var result = ExceptionSanitizer.SanitizeMessage(message);

        // Assert
        Assert.AreEqual(string.Empty, result);
    }

    [TestMethod]
    public void SanitizeException_WithInnerException_SanitizesBoth()
    {
        // Arrange
        var inner = new InvalidOperationException("Inner error");
        var outer = new AggregateException("Outer error", inner);

        // Act
        var result = ExceptionSanitizer.SanitizeException(outer);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Message.Contains("Outer error"));
    }
}
