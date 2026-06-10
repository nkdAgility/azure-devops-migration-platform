// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using Microsoft.Extensions.Logging;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestDsl.Logging;

/// <summary>
/// Assertion helpers for verifying <see cref="ILogger{T}"/> mock invocations.
/// </summary>
internal static class LoggerAssertions
{
    /// <summary>
    /// Verifies that at least one <c>LogWarning</c> call was made on the given logger mock
    /// whose formatted message contains <paramref name="expectedFragment"/>.
    /// </summary>
    internal static void VerifyWarningContaining<T>(
        Mock<ILogger<T>> loggerMock,
        string expectedFragment)
    {
        loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) =>
                    v.ToString()!.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce,
            $"Expected at least one LogWarning containing '{expectedFragment}'.");
    }
}
