// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;

using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Agent.Identity;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Modules;

[TestClass]
public class IdentitiesModuleTests
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed record TestPackageAddress(string RelativePath) : IPackageContentAddress;

    private static PackageContentContext ContentAt(string path)
        => new(PackageContentKind.Artefact, "test-org", "test-project", "Identities", Address: new TestPackageAddress(path));

    private static IdentitiesModule CreateModule(
        IdentitiesModuleOptions? options = null,
        IIdentitySource? identitySource = null,
        string sourceProject = "TestProject",
        IPackageAccess? package = null)
    {
        options ??= new IdentitiesModuleOptions { Enabled = true };
        return new IdentitiesModule(
            NullLogger<IdentitiesModule>.Instance,
            Options.Create(options),
            sourceEndpointInfo: CreateSourceEndpointInfo(sourceProject),
            orchestrator: new IdentitiesOrchestrator(
                NullLogger<IdentitiesOrchestrator>.Instance,
                package: package ?? PackageTestFactory.CreateLooseMock().Object),
            identitySource: identitySource);
    }

    private static IAgentJobContext CreateAgentJobContext()
    {
        var mock = new Mock<IAgentJobContext>();
        mock.SetupGet(x => x.PackagePath).Returns("/tmp/test-package");
        mock.SetupGet(x => x.Mode).Returns("Export");
        mock.SetupGet(x => x.ConfigVersion).Returns("2.0");
        return mock.Object;
    }

    private static ISourceEndpointInfo CreateSourceEndpointInfo(string sourceProject = "TestProject")
    {
        var mock = new Mock<ISourceEndpointInfo>();
        mock.SetupGet(x => x.Url).Returns("https://dev.azure.com/test");
        mock.SetupGet(x => x.Project).Returns(sourceProject);
        mock.SetupGet(x => x.ConnectorType).Returns("Simulated");
        mock.SetupGet(x => x.OrganisationSlug).Returns("test");
        return mock.Object;
    }

    private static ExportContext CreateExportContext(IPackageAccess package)
    {
        return new ExportContext
        {
            Job = new Job { Kind = JobKind.Export },
            Package = package,
            ProgressSink = Mock.Of<IProgressSink>()
        };
    }

    private static ImportContext CreateImportContext(IPackageAccess package)
    {
        return new ImportContext
        {
            Job = new Job { Kind = JobKind.Import },
            Package = package,
            ProgressSink = Mock.Of<IProgressSink>()
        };
    }

    private static ValidationContext CreateValidationContext(IPackageAccess package)
    {
        return new ValidationContext
        {
            Job = new Job(),
            Package = package
        };
    }

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task ExportAsync_WritesDescriptorsJsonl_WhenIdentitiesExist()
    {
        // Arrange
        var storeMock = PackageTestFactory.CreateLooseMock();
        var appendedContent = new System.Text.StringBuilder();
        storeMock
            .Setup(p => p.AppendContentAsync(It.Is<PackageContentContext>(c => c.Module == "Identities" && c.Address!.RelativePath == "descriptors.jsonl"), It.IsAny<PackagePayload>(), It.IsAny<CancellationToken>()))
            .Callback<PackageContentContext, PackagePayload, CancellationToken>((_, payload, _) =>
            {
                payload.Content.Position = 0;
                appendedContent.Append(new StreamReader(payload.Content).ReadToEnd());
            })
            .Returns(ValueTask.CompletedTask);

        var source = new StubIdentitySource(new[]
        {
            new IdentityDescriptor("desc-1", "Alice", "alice@src.com", "User", "Simulated", true),
            new IdentityDescriptor("desc-2", "Bob", "bob@src.com", "User", "Simulated", true),
        });

        var module = CreateModule(identitySource: source, package: storeMock.Object);
        var context = CreateExportContext(storeMock.Object);

        // Act
        await module.ExportAsync(context, CancellationToken.None);

        // Assert — AppendAsync should have been called twice (once per identity)
        storeMock.Verify(
            p => p.AppendContentAsync(It.Is<PackageContentContext>(c => c.Module == "Identities" && c.Address!.RelativePath == "descriptors.jsonl"), It.IsAny<PackagePayload>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        var lines = appendedContent.ToString().Split('\n', System.StringSplitOptions.RemoveEmptyEntries);
        Assert.AreEqual(2, lines.Length);
    }

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task ExportAsync_Skips_WhenModuleDisabled()
    {
        // Arrange
        var storeMock = new Mock<IPackageAccess>(MockBehavior.Strict);
        var source = new StubIdentitySource(new[] { new IdentityDescriptor("d", "A", "a@b.com", "User", "Sim", true) });
        var module = CreateModule(new IdentitiesModuleOptions { Enabled = false }, source, package: storeMock.Object);
        var context = CreateExportContext(storeMock.Object);

        // Act — should not throw or call store
        await module.ExportAsync(context, CancellationToken.None);

        storeMock.VerifyNoOtherCalls();
    }

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task ExportAsync_Skips_WhenNoIdentitySourceRegistered()
    {
        // Arrange
        var storeMock = new Mock<IPackageAccess>(MockBehavior.Strict);
        var module = CreateModule(package: storeMock.Object); // no identity source
        var context = CreateExportContext(storeMock.Object);

        // Act — should not throw or call store
        await module.ExportAsync(context, CancellationToken.None);

        storeMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    // TODO: [test-validity] Score 12/25 — No real assertion beyond "no exception thrown". Rewrite to test:
    // assert the identity mapping service was NOT initialised (descriptors absent → skip import setup), or
    // assert that a structured warning was emitted via IProgressSink/ILogger.
    public async Task ImportAsync_LogsWhenDescriptorsMissing()
    {
        // Arrange
        var storeMock = PackageTestFactory.CreateLooseMock();

        var module = CreateModule(package: storeMock.Object);
        var context = CreateImportContext(storeMock.Object);

        // Act — should not throw
        await module.ImportAsync(context, CancellationToken.None);
    }

    [TestMethod]
    // TODO: [test-validity] Score 12/25 — "No exception = pass" comment reveals there are no real assertions.
    // Rewrite to assert observable state: e.g. verify IIdentityMappingService.LoadAsync was called with correct path,
    // or assert the resulting mapping contains expected identity count.
    public async Task ImportAsync_LoadsMappingWhenDescriptorsPresent()
    {
        // Arrange
        var descriptor = new IdentityDescriptor("desc-1", "Alice", "alice@src.com", "User", "Sim", true);
        var line = JsonSerializer.Serialize(descriptor, s_jsonOptions);

        var storeMock = PackageTestFactory.CreateLooseMock();
        storeMock
            .Setup(p => p.RequestContentAsync(It.Is<PackageContentContext>(c => c.Module == "Identities" && c.Address!.RelativePath == "descriptors.jsonl"), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext _, CancellationToken _) => ValueTask.FromResult<PackagePayload?>(new PackagePayload(new MemoryStream(Encoding.UTF8.GetBytes(line + "\n")))));

        var module = CreateModule(package: storeMock.Object);
        var context = CreateImportContext(storeMock.Object);

        // Act
        await module.ImportAsync(context, CancellationToken.None);
        // No exception = pass
    }

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task ValidateAsync_AddsError_WhenDescriptorsFileMissing()
    {
        // Arrange
        var storeMock = PackageTestFactory.CreateLooseMock();

        var module = CreateModule(package: storeMock.Object);
        var context = CreateValidationContext(storeMock.Object);

        // Act
        await module.ValidateAsync(context, CancellationToken.None);

        // Assert
        Assert.AreEqual(1, context.Errors.Count);
        StringAssert.Contains(context.Errors[0].Message, "descriptors.jsonl");
    }

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task ValidateAsync_AddsError_WhenDescriptorsContainsMalformedJson()
    {
        // Arrange
        var storeMock = PackageTestFactory.CreateLooseMock();
        storeMock
            .Setup(p => p.RequestContentAsync(It.Is<PackageContentContext>(c => c.Module == "Identities" && c.Address!.RelativePath == "descriptors.jsonl"), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext _, CancellationToken _) => ValueTask.FromResult<PackagePayload?>(new PackagePayload(new MemoryStream(Encoding.UTF8.GetBytes("{\"descriptor\":\"d1\"}\nnot-valid-json\n")))));

        var module = CreateModule(package: storeMock.Object);
        var context = CreateValidationContext(storeMock.Object);

        // Act
        await module.ValidateAsync(context, CancellationToken.None);

        // Assert — second line is malformed
        Assert.AreEqual(1, context.Errors.Count);
        StringAssert.Contains(context.Errors[0].Message, "malformed");
    }

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task ValidateAsync_PassesForValidDescriptors()
    {
        // Arrange
        var descriptor = new IdentityDescriptor("desc-1", "Alice", "alice@src.com", "User", "Sim", true);
        var line = JsonSerializer.Serialize(descriptor, s_jsonOptions);

        var storeMock = PackageTestFactory.CreateLooseMock();
        storeMock
            .Setup(p => p.RequestContentAsync(It.Is<PackageContentContext>(c => c.Module == "Identities" && c.Address!.RelativePath == "descriptors.jsonl"), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext _, CancellationToken _) => ValueTask.FromResult<PackagePayload?>(new PackagePayload(new MemoryStream(Encoding.UTF8.GetBytes(line + "\n")))));

        var module = CreateModule(package: storeMock.Object);
        var context = CreateValidationContext(storeMock.Object);

        // Act
        await module.ValidateAsync(context, CancellationToken.None);

        // Assert
        Assert.AreEqual(0, context.Errors.Count);
    }

    // ── T024g: Prepare/Mapping resilience tests ───────────────────────────────

    [TestMethod]
    [TestCategory("UnitTests")]
    public async Task ImportAsync_CompletesWithoutThrowing_WhenMappingJsonAbsent()
    {
        // Arrange — descriptors present, mapping.json missing
        var descriptor = new IdentityDescriptor("desc-1", "Alice", "alice@src.com", "User", "Sim", true);
        var line = JsonSerializer.Serialize(descriptor, s_jsonOptions);

        var storeMock = new Mock<ITestArtefactStore>(MockBehavior.Loose);
        var package = PackageTestFactory.CreateDelegatingMock(storeMock.Object);
        storeMock
            .Setup(s => s.ReadAsync("Identities/descriptors.jsonl", It.IsAny<CancellationToken>()))
            .ReturnsAsync(line + "\n");
        storeMock
            .Setup(s => s.ReadAsync("Identities/mapping.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);  // mapping absent

        var module = CreateModule(package: package.Object);
        var context = CreateImportContext(package.Object);

        // Act — must not throw; missing mapping is a warning, not an error
        await module.ImportAsync(context, CancellationToken.None);
    }

    // TODO: [test-validity] Score 14/25 — No assertions beyond "no exception thrown". Rewrite to test:
    // assert that the identity mapping service has zero entries loaded when both files are absent (first-run state
    // is verifiably empty), or assert that IProgressSink emits a structured warning when descriptors are missing.
    [TestMethod]
    [TestCategory("UnitTests")]
    public async Task ImportAsync_CompletesWithoutThrowing_WhenBothDescriptorsAndMappingAbsent()
    {
        // Arrange — store returns null for everything
        var storeMock = new Mock<ITestArtefactStore>(MockBehavior.Loose);
        var package = PackageTestFactory.CreateDelegatingMock(storeMock.Object);
        storeMock
            .Setup(s => s.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var module = CreateModule(package: package.Object);
        var context = CreateImportContext(package.Object);

        // Act — module must tolerate empty state (e.g. first run, prepare-only scenario)
        await module.ImportAsync(context, CancellationToken.None);
    }

    // TODO: [test-validity] Score 12/25 — No assertions beyond "no exception thrown". Rewrite to test:
    // after ImportAsync runs with a mapping.json present, assert that IIdentityMappingService.Resolve("desc-1")
    // returns "alice@target.com" (verify the mapping was actually applied, not just loaded without error).
    [TestMethod]
    [TestCategory("UnitTests")]
    public async Task ImportAsync_AppliesMapping_WhenBothDescriptorsAndMappingPresent()
    {
        // Arrange
        var descriptor = new IdentityDescriptor("desc-1", "Alice", "alice@src.com", "User", "Sim", true);
        var descriptorLine = JsonSerializer.Serialize(descriptor, s_jsonOptions);

        // Mapping: source descriptor → target identity
        var mappingJson = JsonSerializer.Serialize(
            new Dictionary<string, string> { ["desc-1"] = "alice@target.com" },
            s_jsonOptions);

        var storeMock = new Mock<ITestArtefactStore>(MockBehavior.Loose);
        var package = PackageTestFactory.CreateDelegatingMock(storeMock.Object);
        storeMock
            .Setup(s => s.ReadAsync("Identities/descriptors.jsonl", It.IsAny<CancellationToken>()))
            .ReturnsAsync(descriptorLine + "\n");
        storeMock
            .Setup(s => s.ReadAsync("Identities/mapping.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(mappingJson);

        var module = CreateModule(package: package.Object);
        var context = CreateImportContext(package.Object);

        // Act — should load and apply mapping without error
        await module.ImportAsync(context, CancellationToken.None);
    }

    // ── Stub helpers ──────────────────────────────────────────────────────────

    private sealed class StubIdentitySource : IIdentitySource
    {
        private readonly IdentityDescriptor[] _identities;

        public StubIdentitySource(IdentityDescriptor[] identities)
            => _identities = identities;

        public async IAsyncEnumerable<IdentityDescriptor> EnumerateIdentitiesAsync(
            string projectName,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var id in _identities)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return id;
                await Task.Yield();
            }
        }
    }
}

