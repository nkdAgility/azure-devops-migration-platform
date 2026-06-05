// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Identity;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Identity.Strategies;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Storage.Package;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Identity;

[TestClass]
[TestCategory("UnitTests")]
public sealed class IdentitiesOrchestratorPrepareTests
{
    private const string Org = "test";
    private const string Project = "TestProject";

    private static readonly JsonSerializerOptions Camel = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static IReadOnlyList<IdentityCandidate> Candidates(params IdentityCandidate[] items) => items;

    private static IdentitiesOrchestrator CreateOrchestrator(IPackageAccess package, IIdentityAdapter adapter) =>
        new(
            NullLogger<IdentitiesOrchestrator>.Instance,
            package: package,
            identityAdapter: adapter,
            matchingStrategies: new IIdentityMatchingStrategy[]
            {
                new UpnIdentityMatchingStrategy(),
                new DisplayNameIdentityMatchingStrategy()
            });

    private static async Task SeedDescriptorsAsync(IPackageAccess package, params IdentityDescriptor[] descriptors)
    {
        var sb = new StringBuilder();
        foreach (var d in descriptors)
            sb.Append(JsonSerializer.Serialize(d, Camel)).Append('\n');

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()), writable: false);
        await package.PersistContentAsync(
            new PackageContentContext(PackageContentKind.Artefact, Org, Project, "Identities", new RelativePathAddress("descriptors.jsonl")),
            new PackagePayload(stream, "application/x-ndjson"),
            CancellationToken.None);
    }

    private static async Task<string?> ReadReportAsync(IPackageAccess package)
    {
        var payload = await package.RequestContentAsync(
            new PackageContentContext(PackageContentKind.Artefact, Org, Project, "Identities", new RelativePathAddress("prepare-report.json")),
            CancellationToken.None);
        if (payload is null) return null;
        if (payload.Content.CanSeek) payload.Content.Position = 0;
        using var reader = new StreamReader(payload.Content);
        return await reader.ReadToEndAsync();
    }

    private static PrepareContext PrepareContextFor(IPackageAccess package)
    {
        var target = new Mock<ITargetEndpointInfo>();
        target.SetupGet(x => x.Project).Returns(Project);
        return new PrepareContext
        {
            Job = new Job { JobId = "job-1", Kind = JobKind.Import },
            Package = package,
            ProgressSink = Mock.Of<IProgressSink>(),
            TargetEndpoint = target.Object
        };
    }

    private static IdentityDescriptor Descriptor(string descriptor, string displayName, string uniqueName) =>
        new(descriptor, displayName, uniqueName, SourceType: "user", Origin: "aad", IsActive: true);

    [TestMethod]
    public async Task PrepareAsync_UpnMatch_CachesResolvedTarget()
    {
        using var package = new InMemoryPackageAccess();
        await SeedDescriptorsAsync(package, Descriptor("src-bob", "Bob Smith", "bob@source.com"));

        var adapter = new Mock<IIdentityAdapter>(MockBehavior.Strict);
        adapter.Setup(a => a.FindByUpnAsync("bob@source.com", Project, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Candidates(new IdentityCandidate("tgt-bob", "bob@source.com", "Bob Smith")));

        var orchestrator = CreateOrchestrator(package, adapter.Object);
        await orchestrator.PrepareAsync(PrepareContextFor(package), Org, Project, CancellationToken.None);

        Assert.AreEqual("tgt-bob", orchestrator.ResolvePrepared("bob@source.com"));
        Assert.AreEqual("tgt-bob", orchestrator.ResolvePrepared("src-bob"));
    }

    [TestMethod]
    public async Task PrepareAsync_DisplayNameMatch_WhenNoUpn_CachesResolvedTarget()
    {
        using var package = new InMemoryPackageAccess();
        await SeedDescriptorsAsync(package, Descriptor("src-alice", "Alice Jones", "alice@source.com"));

        var adapter = new Mock<IIdentityAdapter>(MockBehavior.Strict);
        adapter.Setup(a => a.FindByUpnAsync("alice@source.com", Project, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Candidates());
        adapter.Setup(a => a.FindByDisplayNameAsync("Alice Jones", Project, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Candidates(new IdentityCandidate("tgt-alice", "alice@target.com", "Alice Jones")));

        var orchestrator = CreateOrchestrator(package, adapter.Object);
        await orchestrator.PrepareAsync(PrepareContextFor(package), Org, Project, CancellationToken.None);

        Assert.AreEqual("tgt-alice", orchestrator.ResolvePrepared("alice@source.com"));
    }

    [TestMethod]
    public async Task PrepareAsync_AmbiguousDisplayName_LeavesUnresolved()
    {
        using var package = new InMemoryPackageAccess();
        await SeedDescriptorsAsync(package, Descriptor("src-john", "John Smith", "john@source.com"));

        var adapter = new Mock<IIdentityAdapter>(MockBehavior.Strict);
        adapter.Setup(a => a.FindByUpnAsync("john@source.com", Project, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Candidates());
        adapter.Setup(a => a.FindByDisplayNameAsync("John Smith", Project, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Candidates(
                new IdentityCandidate("tgt-j1", "j1@target.com", "John Smith"),
                new IdentityCandidate("tgt-j2", "j2@target.com", "John Smith")));

        var orchestrator = CreateOrchestrator(package, adapter.Object);
        await orchestrator.PrepareAsync(PrepareContextFor(package), Org, Project, CancellationToken.None);

        Assert.IsNull(orchestrator.ResolvePrepared("john@source.com"));
    }

    [TestMethod]
    public async Task PrepareAsync_AdapterThrows_ContinuesAndLeavesUnresolved()
    {
        using var package = new InMemoryPackageAccess();
        await SeedDescriptorsAsync(package, Descriptor("src-err", "Err Person", "err@source.com"));

        var adapter = new Mock<IIdentityAdapter>(MockBehavior.Strict);
        adapter.Setup(a => a.FindByUpnAsync("err@source.com", Project, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestExceptionStub());
        adapter.Setup(a => a.FindByDisplayNameAsync("Err Person", Project, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Candidates());

        var orchestrator = CreateOrchestrator(package, adapter.Object);
        await orchestrator.PrepareAsync(PrepareContextFor(package), Org, Project, CancellationToken.None);

        Assert.IsNull(orchestrator.ResolvePrepared("err@source.com"));
    }

    [TestMethod]
    public async Task PrepareAsync_WritesPrepareReport_WithCounts()
    {
        using var package = new InMemoryPackageAccess();
        await SeedDescriptorsAsync(
            package,
            Descriptor("src-bob", "Bob Smith", "bob@source.com"),
            Descriptor("src-none", "No Match", "none@source.com"));

        var adapter = new Mock<IIdentityAdapter>(MockBehavior.Strict);
        adapter.Setup(a => a.FindByUpnAsync("bob@source.com", Project, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Candidates(new IdentityCandidate("tgt-bob", "bob@source.com", "Bob Smith")));
        adapter.Setup(a => a.FindByUpnAsync("none@source.com", Project, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Candidates());
        adapter.Setup(a => a.FindByDisplayNameAsync("No Match", Project, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Candidates());

        var orchestrator = CreateOrchestrator(package, adapter.Object);
        await orchestrator.PrepareAsync(PrepareContextFor(package), Org, Project, CancellationToken.None);

        var report = await ReadReportAsync(package);
        Assert.IsNotNull(report);
        using var doc = JsonDocument.Parse(report!);
        Assert.AreEqual(1, doc.RootElement.GetProperty("resolvedCount").GetInt32());
        Assert.AreEqual(1, doc.RootElement.GetProperty("unresolvedCount").GetInt32());
        Assert.AreEqual(1, doc.RootElement.GetProperty("upnMatched").GetInt32());
    }

    private sealed class HttpRequestExceptionStub : System.Exception
    {
    }
}
