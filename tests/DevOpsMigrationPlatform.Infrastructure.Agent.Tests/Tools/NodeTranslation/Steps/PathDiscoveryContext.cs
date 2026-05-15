// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited.

using DevOpsMigrationPlatform.Abstractions.Agent.Tools;

using DevOpsMigrationPlatform.Abstractions.Storage;

using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using System.Collections.Generic;

using System.IO;

using System.Text;

using System.Text.Json;

using System.Threading;

using System.Threading.Tasks;



namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.NodeTranslation.Steps;



/// <summary>Shared scenario state for path discovery BDD tests.</summary>

public class PathDiscoveryContext

{

    public Mock<IPackageAccess> PackageMock { get; } = new(MockBehavior.Loose);

    public ReferencedPathTracker? Tracker { get; private set; }



    private readonly List<(string Path, string Content)> _written = new();



    public PathDiscoveryContext()

    {

        PackageMock

            .Setup(s => s.PersistContentAsync(

                It.Is<PackageContentContext>(c => IsReferencedPathsRequest(c)),

                It.IsAny<PackagePayload>(),

                It.IsAny<CancellationToken>()))

            .Callback<PackageContentContext, PackagePayload, CancellationToken>((context, payload, _) => _written.Add((context.Address?.RelativePath ?? string.Empty, ReadAllText(payload.Content))))

            .Returns(ValueTask.CompletedTask);

    }



    public void SetupExistingArtifact(IReadOnlyList<string> areaPaths, IReadOnlyList<string>? iterationPaths = null)

    {

        var artifact = new ReferencedPathsArtifact(areaPaths, iterationPaths ?? new List<string>());

        var json = JsonSerializer.Serialize(artifact, new JsonSerializerOptions

        {

            PropertyNamingPolicy = JsonNamingPolicy.CamelCase

        });

        PackageMock

            .Setup(s => s.RequestContentAsync(

                It.Is<PackageContentContext>(c => IsReferencedPathsRequest(c)),

                It.IsAny<CancellationToken>()))

            .Returns(() => ToPayload(json));

    }



    public void SetupNoExistingArtifact()

    {

        PackageMock

            .Setup(s => s.RequestContentAsync(

                It.Is<PackageContentContext>(c => IsReferencedPathsRequest(c)),

                It.IsAny<CancellationToken>()))

            .Returns(() => ToPayload(null));

    }



    public void CreateTracker()

    {

        Tracker = new ReferencedPathTracker(NullLogger<ReferencedPathTracker>.Instance);

    }



    public int WrittenCount => _written.Count;



    public ReferencedPathsArtifact? GetLastWrittenArtifact()

    {

        if (_written.Count == 0) return null;

        var last = _written[^1];

        return JsonSerializer.Deserialize<ReferencedPathsArtifact>(last.Content, new JsonSerializerOptions

        {

            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,

            PropertyNameCaseInsensitive = true

        });

    }



    private static bool IsReferencedPathsRequest(PackageContentContext context)

        => string.Equals(context.Module, "Nodes", System.StringComparison.OrdinalIgnoreCase)

            && string.Equals(context.Address?.RelativePath, "referenced-paths.json", System.StringComparison.OrdinalIgnoreCase);



    private static string ReadAllText(Stream stream)

    {

        if (stream.CanSeek)

            stream.Position = 0;



        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);

        return reader.ReadToEnd();

    }



    private static ValueTask<PackagePayload?> ToPayload(string? json)

    {

        if (json is null)

            return ValueTask.FromResult<PackagePayload?>(null);



        var bytes = Encoding.UTF8.GetBytes(json);

        return ValueTask.FromResult<PackagePayload?>(new PackagePayload(new MemoryStream(bytes), "application/json"));

    }

}





