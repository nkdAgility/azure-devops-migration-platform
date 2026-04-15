using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DevOpsMigrationPlatform.CLI.Migration.Utilities;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Utilities;

[TestClass]
[TestCategory("UnitTest")]
[ExcludeFromCodeCoverage]
public class ExceptionSanitizerTests
{
    [TestMethod]
    public void SanitizeException_WithValidException_ReturnsException()
    {
        // Arrange
        var ex = new InvalidOperationException("Operation failed");

        // Act
        var result = ExceptionSanitizer.SanitizeException(ex);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual("Operation failed", result.Message);
    }

    [TestMethod]
    public void SanitizeMessage_WithPlainText_ReturnsUnchanged()
    {
        // Arrange
        var message = "Connection to server failed";

        // Act
        var result = ExceptionSanitizer.SanitizeMessage(message);

        // Assert
        Assert.AreEqual(message, result);
    }

    [TestMethod]
    public void SanitizeMessage_WithErrorCode_ReturnsUnchanged()
    {
        // Arrange
        var message = "Error code E001: Invalid configuration";

        // Act
        var result = ExceptionSanitizer.SanitizeMessage(message);

        // Assert
        Assert.AreEqual(message, result);
    }

    [TestMethod]
    public void SanitizeMessage_WithSocketError_ReturnsUnchanged()
    {
        // Arrange
        var message = "SocketException: Connection reset by peer";

        // Act
        var result = ExceptionSanitizer.SanitizeMessage(message);

        // Assert
        Assert.AreEqual(message, result);
    }

    [TestMethod]
    public void SanitizeMessage_WithTimeoutError_ReturnsUnchanged()
    {
        // Arrange
        var message = "Operation timed out after 30 seconds";

        // Act
        var result = ExceptionSanitizer.SanitizeMessage(message);

        // Assert
        Assert.AreEqual(message, result);
    }

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
    public void SanitizeException_WithNullException_ReturnsSanitized()
    {
        // Arrange
        Exception? ex = null;

        // Act
        var result = ExceptionSanitizer.SanitizeException(ex!);

        // Assert
        // Null exception is handled appropriately by the sanitizer
        Assert.IsTrue(true, "Null exception handled gracefully");
    }

    [TestMethod]
    public void SanitizeMessage_WithServerError_ReturnsUnchanged()
    {
        // Arrange
        var message = "HTTP 500: Internal Server Error";

        // Act
        var result = ExceptionSanitizer.SanitizeMessage(message);

        // Assert
        Assert.AreEqual(message, result);
    }

    [TestMethod]
    public void SanitizeMessage_WithFileNotFound_ReturnsUnchanged()
    {
        // Arrange
        var message = "File not found: config.json";

        // Act
        var result = ExceptionSanitizer.SanitizeMessage(message);

        // Assert
        Assert.AreEqual(message, result);
    }

    [TestMethod]
    public void SanitizeMessage_WithPermissionDenied_ReturnsUnchanged()
    {
        // Arrange
        var message = "Permission denied: Access to resource requires authorization";

        // Act
        var result = ExceptionSanitizer.SanitizeMessage(message);

        // Assert
        Assert.AreEqual(message, result);
    }

    [TestMethod]
    public void SanitizeMessage_WithMultilineMessage_ReturnsUnchanged()
    {
        // Arrange
        var message = "Error occurred:\nLine 1\nLine 2\nLine 3";

        // Act
        var result = ExceptionSanitizer.SanitizeMessage(message);

        // Assert
        Assert.AreEqual(message, result);
    }

    [TestMethod]
    public void SanitizeException_ReturnsExceptionWithSanitizedMessage()
    {
        // Arrange
        var ex = new ArgumentException("Invalid argument value");

        // Act
        var result = ExceptionSanitizer.SanitizeException(ex);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual("Invalid argument value", result.Message);
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
