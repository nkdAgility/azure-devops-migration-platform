// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.CLI.Migration.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Testing.Dsl.SystemTests;

/// <summary>
/// Behaviour-named assertions for credential masking via ExceptionSanitizer.
/// </summary>
public static class CredentialMaskingAssert
{
    /// <summary>
    /// Asserts that the given raw PAT value does not appear in <paramref name="output"/>
    /// after sanitisation is applied.
    /// </summary>
    public static void PatIsAbsentFromOutput(string output, string patValue)
    {
        Assert.IsFalse(
            output.Contains(patValue, StringComparison.Ordinal),
            $"PAT value must not appear in output. Output:\n{output}");
    }

    /// <summary>
    /// Asserts that a string containing a bearer token has the token value masked
    /// by ExceptionSanitizer.SanitizeMessage.
    /// </summary>
    public static void BearerTokenIsMaskedByExceptionSanitizer(string rawToken)
    {
        var input = $"Authorization: Bearer {rawToken}";
        var sanitized = ExceptionSanitizer.SanitizeMessage(input);

        Assert.IsFalse(
            sanitized.Contains(rawToken, StringComparison.Ordinal),
            $"Bearer token '{rawToken}' was not masked. Sanitized output: {sanitized}");

        StringAssert.Contains(sanitized, "Bearer ***",
            $"Expected 'Bearer ***' mask in sanitized output. Got: {sanitized}");
    }

    /// <summary>
    /// Asserts that a structured log field value is masked when the field name
    /// matches a known credential field pattern.
    /// </summary>
    public static void CredentialFieldIsMaskedInLogEntry(string logEntry, string sensitiveValue)
    {
        var sanitized = ExceptionSanitizer.SanitizeMessage(logEntry);

        Assert.IsFalse(
            sanitized.Contains(sensitiveValue, StringComparison.Ordinal),
            $"Sensitive value '{sensitiveValue}' was not masked in log entry. " +
            $"Sanitized entry: {sanitized}");
    }
}
