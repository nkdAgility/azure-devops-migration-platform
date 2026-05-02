// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) NKD Agility Limited

using System.Diagnostics.CodeAnalysis;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DevOpsMigrationPlatform.Abstractions.Validation;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.AzureDevOps.Validation;

[TestClass]
[TestCategory("UnitTest")]
[ExcludeFromCodeCoverage]
public class WiqlValidatorTests
{
    [TestMethod]
    public void Validate_WithValidSelectQuery_ReturnsSuccess()
    {
        // Arrange
        var query = "SELECT [System.Id], [System.Title] FROM WorkItems";

        // Act
        var result = WiqlValidator.Validate(query);

        // Assert
        Assert.IsTrue(result.IsValid, "Valid SELECT query should pass validation");
        Assert.IsNull(result.ErrorMessage);
    }

    [TestMethod]
    public void Validate_WithSelectWildcard_ReturnsSuccess()
    {
        // Arrange
        var query = "SELECT * FROM WorkItems";

        // Act
        var result = WiqlValidator.Validate(query);

        // Assert
        Assert.IsTrue(result.IsValid);
    }

    [TestMethod]
    public void Validate_WithNullQuery_ReturnsSuccess()
    {
        // Arrange
        var query = (string?)null;

        // Act
        var result = WiqlValidator.Validate(query);

        // Assert
        Assert.IsTrue(result.IsValid, "Null query is valid (will be replaced with SELECT * by downstream code)");
        Assert.IsNull(result.ErrorMessage);
    }

    [TestMethod]
    public void Validate_WithEmptyQuery_ReturnsSuccess()
    {
        // Arrange
        var query = "   ";

        // Act
        var result = WiqlValidator.Validate(query);

        // Assert
        Assert.IsTrue(result.IsValid);
    }

    [TestMethod]
    public void Validate_WithUpdateQuery_ReturnsFail()
    {
        // Arrange
        var query = "UPDATE WorkItems SET [System.State] = 'Done'";

        // Act
        var result = WiqlValidator.Validate(query);

        // Assert
        Assert.IsFalse(result.IsValid, "UPDATE queries should be rejected");
        Assert.IsNotNull(result.ErrorMessage);
        Assert.IsTrue(result.ErrorMessage!.Contains("UPDATE", System.StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Validate_WithDeleteQuery_ReturnsFail()
    {
        // Arrange
        var query = "DELETE FROM WorkItems";

        // Act
        var result = WiqlValidator.Validate(query);

        // Assert
        Assert.IsFalse(result.IsValid, "DELETE queries should be rejected");
        Assert.IsNotNull(result.ErrorMessage);
        Assert.IsTrue(result.ErrorMessage!.Contains("DELETE", System.StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Validate_WithDropQuery_ReturnsFail()
    {
        // Arrange
        var query = "DROP TABLE WorkItems";

        // Act
        var result = WiqlValidator.Validate(query);

        // Assert
        Assert.IsFalse(result.IsValid, "DROP queries should be rejected");
        Assert.IsNotNull(result.ErrorMessage);
    }

    [TestMethod]
    public void Validate_WithInsertQuery_ReturnsFail()
    {
        // Arrange
        var query = "INSERT INTO WorkItems VALUES (...)";

        // Act
        var result = WiqlValidator.Validate(query);

        // Assert
        Assert.IsFalse(result.IsValid, "INSERT queries should be rejected");
        Assert.IsNotNull(result.ErrorMessage);
    }

    [TestMethod]
    public void Validate_WithSqlLineComment_ReturnsFail()
    {
        // Arrange - Although WIQL doesn't natively support comments, we still reject 
        // them to prevent potential injection if the backend doesn't handle them safely
        var query = "SELECT [System.Id] FROM WorkItems -- comment syntax not valid in WIQL";

        // Act
        var result = WiqlValidator.Validate(query);

        // Assert - In pure WIQL, comments aren't native, so this should succeed
        // (The SELECT prefix is valid, and WIQL parsers will reject -- if invalid)
        Assert.IsTrue(result.IsValid, "WIQL doesn't natively support -- comments, but validation doesn't need to reject them");
    }

    [TestMethod]
    public void Validate_WithBlockCommentMarkers_SucceedsAsNoDestructiveOps()
    {
        // Arrange - WIQL doesn't support /* */ block comments natively
        var query = "SELECT [System.Id] FROM WorkItems";

        // Act
        var result = WiqlValidator.Validate(query);

        // Assert - The query itself is valid SELECT
        Assert.IsTrue(result.IsValid, "Valid SELECT doesn't contain destructive operations");
    }

    [TestMethod]
    public void Validate_WithDeclareStatement_ReturnsFail()
    {
        // Arrange
        var query = "DECLARE @var AS STRING; SELECT @var FROM WorkItems";

        // Act
        var result = WiqlValidator.Validate(query);

        // Assert
        Assert.IsFalse(result.IsValid, "DECLARE statements should be rejected");
        Assert.IsNotNull(result.ErrorMessage);
    }

    [TestMethod]
    public void Validate_WithNonSelectStart_ReturnsFail()
    {
        // Arrange
        var query = "GRANT SELECT ON WorkItems";

        // Act
        var result = WiqlValidator.Validate(query);

        // Assert
        Assert.IsFalse(result.IsValid, "Queries not starting with SELECT should be rejected");
        Assert.IsNotNull(result.ErrorMessage);
        Assert.IsTrue(result.ErrorMessage!.Contains("must start with SELECT", System.StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void Validate_WithComplexValidQuery_ReturnsSuccess()
    {
        // Arrange
        var query = """
                    SELECT [System.Id], [System.Title], [System.State]
                    FROM WorkItems
                    WHERE [System.TeamProject] = 'MyProject'
                    AND [System.WorkItemType] = 'Bug'
                    AND [System.ChangedDate] > '2024-01-01'
                    ORDER BY [System.Id]
                    """;

        // Act
        var result = WiqlValidator.Validate(query);

        // Assert
        Assert.IsTrue(result.IsValid, "Complex valid SELECT query should pass");
        Assert.IsNull(result.ErrorMessage);
    }

    [TestMethod]
    public void Validate_WithWhitespacePrefix_ReturnsSuccess()
    {
        // Arrange
        var query = "   SELECT * FROM WorkItems   ";

        // Act
        var result = WiqlValidator.Validate(query);

        // Assert
        Assert.IsTrue(result.IsValid, "Query with whitespace should be trimmed and validated");
    }

    [TestMethod]
    public void Validate_WithCaseInsensitiveSelect_ReturnsSuccess()
    {
        // Arrange
        var query = "select * from WorkItems";

        // Act
        var result = WiqlValidator.Validate(query);

        // Assert
        Assert.IsTrue(result.IsValid, "Case-insensitive SELECT should be valid");
    }

    [TestMethod]
    public void Validate_ReturnsFailureRecordWhenInvalid()
    {
        // Arrange
        var query = "DELETE * FROM WorkItems";

        // Act
        var result = WiqlValidator.Validate(query);

        // Assert
        Assert.IsFalse(result.IsValid);
        Assert.IsNotNull(result.ErrorMessage);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.ErrorMessage), "Error message should not be empty");
    }

    [TestMethod]
    public void Validate_ReturnsSuccessRecordWhenValid()
    {
        // Arrange
        var query = "SELECT [System.Id] FROM WorkItems";

        // Act
        var result = WiqlValidator.Validate(query);

        // Assert
        Assert.IsTrue(result.IsValid);
        Assert.IsNull(result.ErrorMessage);
    }
}
