// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited.

using DevOpsMigrationPlatform.Abstractions.Agent.Tools;

using DevOpsMigrationPlatform.Abstractions.Options;

using DevOpsMigrationPlatform.Abstractions.Storage;

using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;

using Microsoft.Extensions.Logging.Abstractions;

using Microsoft.Extensions.Options;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Moq;

using System;

using System.Text.Json;

using System.Text.Json.Serialization;

using System.Threading;

using System.Threading.Tasks;



namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.NodeTranslation;



[TestClass]

public class NodeTranslationValidatorTests

{

    private static readonly ProjectMapping DefaultMapping = new("SourceProject", "TargetProject");



    private static readonly JsonSerializerOptions s_json = new()

    {

        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,

        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

        PropertyNameCaseInsensitive = true

    };



    private static (NodeTranslationValidator validator, Mock<IPackageAccess> packageMock)

        CreateValidator(

            NodeTranslationOptions? opts = null,

            string? referencedPathsJson = null)

    {

        opts ??= new NodeTranslationOptions

        {

            Enabled = true,

            AreaPathMappings = [],

            IterationPathMappings = []

        };



        var packageMock = new Mock<IPackageAccess>(MockBehavior.Loose);

        packageMock.Setup(p => p.RequestContentAsync(

                It.Is<PackageContentContext>(c => IsPath(c, "Nodes/referenced-paths.json", "referenced-paths.json")),

                It.IsAny<CancellationToken>()))

            .Returns(() => ToPayload(referencedPathsJson));



        var tool = new NodeTranslationTool(Options.Create(opts), NullLogger<NodeTranslationTool>.Instance);

        var validator = new NodeTranslationValidator(Options.Create(opts), tool, "test-org", "test-project");

        return (validator, packageMock);

    }



    private static string Serialize(ReferencedPathsArtifact artifact)

        => JsonSerializer.Serialize(artifact, s_json);



    [TestCategory("CodeTest")]

    [TestCategory("UnitTests")]



    [TestMethod]

    public async Task ValidateAsync_AllPathsMapped_ReturnsValidReport()

    {

        var opts = new NodeTranslationOptions

        {

            Enabled = true,

            AreaPathMappings = [new NodeMapping(@"^SourceProject\\(.*)", @"TargetProject\$1")],

            IterationPathMappings = []

        };

        var artifact = new ReferencedPathsArtifact(

            new[] { @"SourceProject\Team A" },

            []);



        var (validator, packageMock) = CreateValidator(opts, Serialize(artifact));



        var report = await validator.ValidateAsync(packageMock.Object, DefaultMapping, CancellationToken.None);



        Assert.IsTrue(report.IsValid);

        Assert.AreEqual(0, report.UnmappedPaths.Count);

        Assert.AreEqual(0, report.UnanchoredPaths.Count);

        Assert.AreEqual(0, report.MalformedTargetPaths.Count);

    }



    [TestCategory("CodeTest")]

    [TestCategory("UnitTests")]



    [TestMethod]

    public async Task ValidateAsync_ExternalPath_ReportsUnanchored()

    {

        var artifact = new ReferencedPathsArtifact(

            new[] { @"OtherProject\Team A" },

            []);



        var (validator, packageMock) = CreateValidator(referencedPathsJson: Serialize(artifact));



        var report = await validator.ValidateAsync(packageMock.Object, DefaultMapping, CancellationToken.None);



        Assert.IsFalse(report.IsValid);

        Assert.AreEqual(1, report.UnanchoredPaths.Count);

        Assert.AreEqual(@"OtherProject\Team A", report.UnanchoredPaths[0].Path);

    }



    [TestCategory("CodeTest")]

    [TestCategory("UnitTests")]



    [TestMethod]

    public async Task ValidateAsync_SourceProjectPath_AutoSwapped_ReturnsValid()

    {

        var artifact = new ReferencedPathsArtifact(

            new[] { @"SourceProject\Team A" },

            []);



        var (validator, packageMock) = CreateValidator(referencedPathsJson: Serialize(artifact));



        var report = await validator.ValidateAsync(packageMock.Object, DefaultMapping, CancellationToken.None);



        Assert.IsTrue(report.IsValid, "Auto-swapped path should produce a valid report.");

        Assert.AreEqual(0, report.UnanchoredPaths.Count);

        Assert.AreEqual(0, report.UnmappedPaths.Count);

    }



    [TestCategory("CodeTest")]

    [TestCategory("UnitTests")]



    [TestMethod]

    public async Task ValidateAsync_NoArtifact_ReturnsValidEmptyReport()

    {

        var (validator, packageMock) = CreateValidator(referencedPathsJson: null);



        var report = await validator.ValidateAsync(packageMock.Object, DefaultMapping, CancellationToken.None);



        Assert.IsTrue(report.IsValid);

        Assert.AreEqual(0, report.UnmappedPaths.Count);

        Assert.AreEqual(0, report.UnanchoredPaths.Count);

        Assert.AreEqual(0, report.MalformedTargetPaths.Count);

    }



    [TestCategory("CodeTest")]

    [TestCategory("UnitTests")]



    [TestMethod]

    public async Task ValidateAsync_InvalidRegexPattern_ReportsMalformed()

    {

        var opts = new NodeTranslationOptions

        {

            Enabled = true,

            AreaPathMappings = [new NodeMapping("[", "replacement")],

            IterationPathMappings = []

        };



        var packageMock = new Mock<IPackageAccess>(MockBehavior.Loose);

        packageMock.Setup(p => p.RequestContentAsync(

                It.Is<PackageContentContext>(c => IsPath(c, "Nodes/referenced-paths.json", "referenced-paths.json")),

                It.IsAny<CancellationToken>()))

            .Returns(() => ToPayload(null));



        var toolMock = new Mock<INodeTranslationTool>(MockBehavior.Loose);

        toolMock.Setup(t => t.IsEnabled).Returns(true);



        var validator = new NodeTranslationValidator(Options.Create(opts), toolMock.Object, "test-org", "test-project");



        var report = await validator.ValidateAsync(packageMock.Object, DefaultMapping, CancellationToken.None);



        Assert.IsFalse(report.IsValid);

        Assert.AreEqual(1, report.MalformedTargetPaths.Count);

        Assert.AreEqual("[", report.MalformedTargetPaths[0]);

    }



    [TestCategory("CodeTest")]

    [TestCategory("UnitTests")]



    [TestMethod]

    public async Task ValidateAsync_EmptyArtifact_ReturnsValidReport()

    {

        var artifact = new ReferencedPathsArtifact([], []);

        var (validator, packageMock) = CreateValidator(referencedPathsJson: Serialize(artifact));



        var report = await validator.ValidateAsync(packageMock.Object, DefaultMapping, CancellationToken.None);



        Assert.IsTrue(report.IsValid);

        Assert.AreEqual(0, report.UnmappedPaths.Count);

        Assert.AreEqual(0, report.UnanchoredPaths.Count);

    }



    [TestCategory("CodeTest")]

    [TestCategory("UnitTests")]



    [TestMethod]

    public async Task ValidateAsync_MultipleExternalPaths_AllReported()

    {

        var artifact = new ReferencedPathsArtifact(

            new[] { @"ProjectA\Node1", @"ProjectB\Node2" },

            new[] { @"ProjectC\Sprint 1" });



        var (validator, packageMock) = CreateValidator(referencedPathsJson: Serialize(artifact));



        var report = await validator.ValidateAsync(packageMock.Object, DefaultMapping, CancellationToken.None);



        Assert.IsFalse(report.IsValid);

        Assert.AreEqual(3, report.UnanchoredPaths.Count);

    }



    private static bool IsPath(PackageContentContext context, params string[] candidates)

        => context.Address is not null && Array.Exists(candidates, candidate => string.Equals(context.Address.RelativePath, candidate, StringComparison.OrdinalIgnoreCase));



    private static ValueTask<PackagePayload?> ToPayload(string? json)

    {

        if (json is null)

            return ValueTask.FromResult<PackagePayload?>(null);



        var bytes = System.Text.Encoding.UTF8.GetBytes(json);

        return ValueTask.FromResult<PackagePayload?>(new PackagePayload(new System.IO.MemoryStream(bytes), "application/json"));

    }

}







