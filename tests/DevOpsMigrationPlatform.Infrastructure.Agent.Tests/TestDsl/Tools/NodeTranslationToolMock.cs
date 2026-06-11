// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestDsl.Tools;

/// <summary>
/// Factory methods for <see cref="INodeTranslationTool"/> mocks covering null-translation patterns.
/// </summary>
internal static class NodeTranslationToolMock
{
    /// <summary>
    /// Returns a mock where the given <paramref name="nullPath"/> translates to null
    /// and all other paths receive a project-swap translation
    /// (<paramref name="sourceProject"/> → <paramref name="targetProject"/> substring replace).
    /// </summary>
    internal static Mock<INodeTranslationTool> ReturningNullFor(
        string nullPath,
        string sourceProject = "SourceProject",
        string targetProject = "TargetProject")
    {
        var mock = new Mock<INodeTranslationTool>(MockBehavior.Loose);
        mock.Setup(t => t.IsEnabled).Returns(true);
        mock.Setup(t => t.TranslatePath(
                "System.AreaPath",
                nullPath,
                It.IsAny<ProjectMapping>()))
            .Returns(new PathTranslation(null, false, false, false));
        mock.Setup(t => t.TranslatePath(
                "System.AreaPath",
                It.Is<string>(p => p != nullPath),
                It.IsAny<ProjectMapping>()))
            .Returns<string, string, ProjectMapping>((_, path, _) =>
                new PathTranslation(
                    path.Replace(sourceProject, targetProject, StringComparison.Ordinal),
                    false, true, false));
        return mock;
    }

    /// <summary>
    /// Returns a mock where every path translates to null.
    /// </summary>
    internal static Mock<INodeTranslationTool> ReturningNullForAll()
    {
        var mock = new Mock<INodeTranslationTool>(MockBehavior.Loose);
        mock.Setup(t => t.IsEnabled).Returns(true);
        mock.Setup(t => t.TranslatePath(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<ProjectMapping>()))
            .Returns(new PathTranslation(null, false, false, false));
        return mock;
    }
}
