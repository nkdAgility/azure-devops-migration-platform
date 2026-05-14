// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.NodeTranslation.Steps;

/// <summary>Shared scenario state for path discovery BDD tests.</summary>
public class PathDiscoveryContext
{
    public Mock<IArtefactStore> ArtefactStoreMock { get; } = new(MockBehavior.Loose);
    public ReferencedPathTracker? Tracker { get; private set; }

    private readonly List<(string Path, string Content)> _written = new();

    public PathDiscoveryContext()
    {
        ArtefactStoreMock
            .Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((p, c, _) => _written.Add((p, c)))
            .Returns(Task.CompletedTask);
    }

    public void SetupExistingArtifact(IReadOnlyList<string> areaPaths, IReadOnlyList<string>? iterationPaths = null)
    {
        var artifact = new ReferencedPathsArtifact(areaPaths, iterationPaths ?? new List<string>());
        var json = JsonSerializer.Serialize(artifact, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        ArtefactStoreMock
            .Setup(s => s.ReadAsync("Nodes/referenced-paths.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);
    }

    public void SetupNoExistingArtifact()
    {
        ArtefactStoreMock
            .Setup(s => s.ReadAsync("Nodes/referenced-paths.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
    }

    public void CreateTracker()
    {
        Tracker = new ReferencedPathTracker(NullLogger<ReferencedPathTracker>.Instance);
    }

    public int WrittenCount => _written.Count;

    public ReferencedPathsArtifact? GetLastWrittenArtifact()
    {
        if (_written.Count == 0) return null;
        var last = _written[_written.Count - 1];
        return JsonSerializer.Deserialize<ReferencedPathsArtifact>(last.Content, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        });
    }
}
