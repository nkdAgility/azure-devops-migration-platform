// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited.

using DevOpsMigrationPlatform.Abstractions.Agent.Tools;

using DevOpsMigrationPlatform.Abstractions.Storage;

using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;

using Microsoft.Extensions.Logging.Abstractions;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Moq;

using System.Collections.Generic;

using System.Text.Json;

using System.Threading;

using System.Threading.Tasks;



namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.NodeTranslation;



[TestClass]

public class ReferencedPathTrackerTests

{

    private const string DefaultOrganisation = "https://dev.azure.com/fabrikam";

    private const string DefaultProject = "ProjectA";



    private static Mock<IPackageAccess> CreatePackageMock(string? existingJson = null)

    {

        var mock = new Mock<IPackageAccess>(MockBehavior.Loose);

        mock.Setup(s => s.RequestContentAsync(

                It.Is<PackageContentContext>(c => IsReferencedPathsRequest(c)),

                It.IsAny<CancellationToken>()))

            .Returns(() => ToPayload(existingJson));

        mock.Setup(s => s.PersistContentAsync(

                It.Is<PackageContentContext>(c => IsReferencedPathsRequest(c)),

                It.IsAny<PackagePayload>(),

                It.IsAny<CancellationToken>()))

            .Returns(ValueTask.CompletedTask);

        return mock;

    }



    private static ReferencedPathTracker CreateTracker()

        => new ReferencedPathTracker(NullLogger<ReferencedPathTracker>.Instance);



    [TestMethod]

    public async Task RecordAreaPathAsync_NewPath_WritesArtifact()

    {

        var packageMock = CreatePackageMock();

        var tracker = CreateTracker();



        await tracker.RecordAreaPathAsync(@"ProjectA\Team A", packageMock.Object, DefaultOrganisation, DefaultProject, CancellationToken.None);



        packageMock.Verify(s => s.PersistContentAsync(

            It.Is<PackageContentContext>(c => IsReferencedPathsRequest(c)),

            It.IsAny<PackagePayload>(),

            It.IsAny<CancellationToken>()), Times.Once);

        Assert.AreEqual(1, tracker.AreaPaths.Count);

        Assert.IsTrue(tracker.AreaPaths.Contains(@"ProjectA\Team A"));

    }



    [TestMethod]

    public async Task RecordAreaPathAsync_DuplicatePath_DoesNotWriteAgain()

    {

        var packageMock = CreatePackageMock();

        var tracker = CreateTracker();



        await tracker.RecordAreaPathAsync(@"ProjectA\Team A", packageMock.Object, DefaultOrganisation, DefaultProject, CancellationToken.None);

        await tracker.RecordAreaPathAsync(@"ProjectA\Team A", packageMock.Object, DefaultOrganisation, DefaultProject, CancellationToken.None);



        packageMock.Verify(s => s.PersistContentAsync(

            It.Is<PackageContentContext>(c => IsReferencedPathsRequest(c)),

            It.IsAny<PackagePayload>(),

            It.IsAny<CancellationToken>()), Times.Once);

        Assert.AreEqual(1, tracker.AreaPaths.Count);

    }



    [TestMethod]

    public async Task RecordAreaPathAsync_CaseInsensitiveDuplicate_DoesNotWriteAgain()

    {

        var packageMock = CreatePackageMock();

        var tracker = CreateTracker();



        await tracker.RecordAreaPathAsync(@"ProjectA\Team A", packageMock.Object, DefaultOrganisation, DefaultProject, CancellationToken.None);

        await tracker.RecordAreaPathAsync(@"PROJECTA\TEAM A", packageMock.Object, DefaultOrganisation, DefaultProject, CancellationToken.None);



        packageMock.Verify(s => s.PersistContentAsync(

            It.Is<PackageContentContext>(c => IsReferencedPathsRequest(c)),

            It.IsAny<PackagePayload>(),

            It.IsAny<CancellationToken>()), Times.Once);

        Assert.AreEqual(1, tracker.AreaPaths.Count);

    }



    [TestMethod]

    public async Task InitializeAsync_LoadsExistingPaths()

    {

        var artifact = new ReferencedPathsArtifact(

            new List<string> { @"ProjectA\Team A" },

            new List<string> { @"ProjectA\Sprint 1" });

        var json = JsonSerializer.Serialize(artifact, new JsonSerializerOptions

        {

            PropertyNamingPolicy = JsonNamingPolicy.CamelCase

        });

        var packageMock = CreatePackageMock(json);

        var tracker = CreateTracker();



        await tracker.InitializeAsync(packageMock.Object, DefaultOrganisation, DefaultProject, CancellationToken.None);



        Assert.AreEqual(1, tracker.AreaPaths.Count);

        Assert.AreEqual(1, tracker.IterationPaths.Count);

        Assert.IsTrue(tracker.AreaPaths.Contains(@"ProjectA\Team A"));

    }



    [TestMethod]

    public async Task InitializeAsync_ThenRecordExisting_DoesNotWriteAgain()

    {

        var artifact = new ReferencedPathsArtifact(

            new List<string> { @"ProjectA\Team A" },

            new List<string>());

        var json = JsonSerializer.Serialize(artifact, new JsonSerializerOptions

        {

            PropertyNamingPolicy = JsonNamingPolicy.CamelCase

        });

        var packageMock = CreatePackageMock(json);

        var tracker = CreateTracker();



        await tracker.InitializeAsync(packageMock.Object, DefaultOrganisation, DefaultProject, CancellationToken.None);

        await tracker.RecordAreaPathAsync(@"ProjectA\Team A", packageMock.Object, DefaultOrganisation, DefaultProject, CancellationToken.None);



        packageMock.Verify(s => s.PersistContentAsync(

            It.Is<PackageContentContext>(c => IsReferencedPathsRequest(c)),

            It.IsAny<PackagePayload>(),

            It.IsAny<CancellationToken>()), Times.Never);

    }



    private static bool IsReferencedPathsRequest(PackageContentContext context)

        => string.Equals(context.Module, "Nodes", System.StringComparison.OrdinalIgnoreCase)

            && string.Equals(context.Address?.RelativePath, "referenced-paths.json", System.StringComparison.OrdinalIgnoreCase);



    private static ValueTask<PackagePayload?> ToPayload(string? json)

    {

        if (json is null)

            return ValueTask.FromResult<PackagePayload?>(null);



        var bytes = System.Text.Encoding.UTF8.GetBytes(json);

        return ValueTask.FromResult<PackagePayload?>(new PackagePayload(new System.IO.MemoryStream(bytes), "application/json"));

    }

}





