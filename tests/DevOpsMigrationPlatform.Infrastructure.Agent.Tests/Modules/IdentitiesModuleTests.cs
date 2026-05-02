using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;

using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Agent.Identity;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using Microsoft.Extensions.Configuration;
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

    private static IdentitiesModule CreateModule(
        IdentitiesModuleOptions? options = null,
        IIdentitySource? identitySource = null,
        JobConfiguration? activeJobConfig = null)
    {
        options ??= new IdentitiesModuleOptions { Enabled = true };
        activeJobConfig ??= CreateActiveJobConfig();
        return new IdentitiesModule(
            NullLogger<IdentitiesModule>.Instance,
            Options.Create(options),
            sourceEndpointInfo: CreateSourceEndpointInfo(activeJobConfig),
            orchestrator: new IdentitiesOrchestrator(NullLogger<IdentitiesOrchestrator>.Instance),
            identitySource: identitySource);
    }

    private static JobConfiguration CreateActiveJobConfig(string sourceProject = "TestProject")
    {
        var state = new JobConfiguration();
        state.PackageConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MigrationPlatform:Source:Type"] = "Simulated",
                ["MigrationPlatform:Source:Project"] = sourceProject,
            })
            .Build();
        return state;
    }

    private static IAgentJobContext CreateAgentJobContext(JobConfiguration activeJobConfig)
    {
        var mock = new Mock<IAgentJobContext>();
        mock.SetupGet(x => x.PackagePath).Returns("/tmp/test-package");
        mock.SetupGet(x => x.Mode).Returns("Export");
        mock.SetupGet(x => x.ConfigVersion).Returns("2.0");
        return mock.Object;
    }

    private static ISourceEndpointInfo CreateSourceEndpointInfo(JobConfiguration activeJobConfig)
    {
        var mock = new Mock<ISourceEndpointInfo>();
        mock.SetupGet(x => x.Url).Returns("https://dev.azure.com/test");
        mock.SetupGet(x => x.Project).Returns(activeJobConfig.PackageConfig?["MigrationPlatform:Source:Project"] ?? "TestProject");
        mock.SetupGet(x => x.ConnectorType).Returns("Simulated");
        return mock.Object;
    }

    private static ExportContext CreateExportContext(Mock<IArtefactStore> store)
    {
        return new ExportContext
        {
            Job = new Job { Kind = JobKind.Export },
            ArtefactStore = store.Object,
            StateStore = Mock.Of<IStateStore>(),
            ProgressSink = Mock.Of<IProgressSink>()
        };
    }

    private static ImportContext CreateImportContext(Mock<IArtefactStore> store)
    {
        return new ImportContext
        {
            Job = new Job { Kind = JobKind.Import },
            ArtefactStore = store.Object,
            StateStore = Mock.Of<IStateStore>(),
            ProgressSink = Mock.Of<IProgressSink>()
        };
    }

    private static ValidationContext CreateValidationContext(Mock<IArtefactStore> store)
    {
        return new ValidationContext
        {
            Job = new Job(),
            ArtefactStore = store.Object
        };
    }

    [TestMethod]
    public async Task ExportAsync_WritesDescriptorsJsonl_WhenIdentitiesExist()
    {
        // Arrange
        var storeMock = new Mock<IArtefactStore>(MockBehavior.Loose);
        var appendedContent = new System.Text.StringBuilder();
        storeMock
            .Setup(s => s.AppendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((path, content, _) => appendedContent.Append(content))
            .Returns(Task.CompletedTask);

        var source = new StubIdentitySource(new[]
        {
            new IdentityDescriptor("desc-1", "Alice", "alice@src.com", "User", "Simulated", true),
            new IdentityDescriptor("desc-2", "Bob", "bob@src.com", "User", "Simulated", true),
        });

        var module = CreateModule(identitySource: source, activeJobConfig: CreateActiveJobConfig());
        var context = CreateExportContext(storeMock);

        // Act
        await module.ExportAsync(context, CancellationToken.None);

        // Assert — AppendAsync should have been called twice (once per identity)
        storeMock.Verify(
            s => s.AppendAsync("Identities/descriptors.jsonl", It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        var lines = appendedContent.ToString().Split('\n', System.StringSplitOptions.RemoveEmptyEntries);
        Assert.AreEqual(2, lines.Length);
    }

    [TestMethod]
    public async Task ExportAsync_Skips_WhenModuleDisabled()
    {
        // Arrange
        var storeMock = new Mock<IArtefactStore>(MockBehavior.Strict);
        var source = new StubIdentitySource(new[] { new IdentityDescriptor("d", "A", "a@b.com", "User", "Sim", true) });
        var module = CreateModule(new IdentitiesModuleOptions { Enabled = false }, source);
        var context = CreateExportContext(storeMock);

        // Act — should not throw or call store
        await module.ExportAsync(context, CancellationToken.None);

        storeMock.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task ExportAsync_Skips_WhenNoIdentitySourceRegistered()
    {
        // Arrange
        var storeMock = new Mock<IArtefactStore>(MockBehavior.Strict);
        var module = CreateModule(); // no identity source
        var context = CreateExportContext(storeMock);

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
        var storeMock = new Mock<IArtefactStore>(MockBehavior.Loose);
        storeMock
            .Setup(s => s.ReadAsync("Identities/descriptors.jsonl", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var module = CreateModule();
        var context = CreateImportContext(storeMock);

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

        var storeMock = new Mock<IArtefactStore>(MockBehavior.Loose);
        storeMock
            .Setup(s => s.ReadAsync("Identities/descriptors.jsonl", It.IsAny<CancellationToken>()))
            .ReturnsAsync(line + "\n");
        storeMock
            .Setup(s => s.ReadAsync("Identities/mapping.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var module = CreateModule();
        var context = CreateImportContext(storeMock);

        // Act
        await module.ImportAsync(context, CancellationToken.None);
        // No exception = pass
    }

    [TestMethod]
    public async Task ValidateAsync_AddsError_WhenDescriptorsFileMissing()
    {
        // Arrange
        var storeMock = new Mock<IArtefactStore>(MockBehavior.Loose);
        storeMock
            .Setup(s => s.ExistsAsync("Identities/descriptors.jsonl", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var module = CreateModule();
        var context = CreateValidationContext(storeMock);

        // Act
        await module.ValidateAsync(context, CancellationToken.None);

        // Assert
        Assert.AreEqual(1, context.Errors.Count);
        StringAssert.Contains(context.Errors[0].Message, "descriptors.jsonl");
    }

    [TestMethod]
    public async Task ValidateAsync_AddsError_WhenDescriptorsContainsMalformedJson()
    {
        // Arrange
        var storeMock = new Mock<IArtefactStore>(MockBehavior.Loose);
        storeMock
            .Setup(s => s.ExistsAsync("Identities/descriptors.jsonl", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        storeMock
            .Setup(s => s.ReadAsync("Identities/descriptors.jsonl", It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"descriptor\":\"d1\"}\nnot-valid-json\n");

        var module = CreateModule();
        var context = CreateValidationContext(storeMock);

        // Act
        await module.ValidateAsync(context, CancellationToken.None);

        // Assert — second line is malformed
        Assert.AreEqual(1, context.Errors.Count);
        StringAssert.Contains(context.Errors[0].Message, "malformed");
    }

    [TestMethod]
    public async Task ValidateAsync_PassesForValidDescriptors()
    {
        // Arrange
        var descriptor = new IdentityDescriptor("desc-1", "Alice", "alice@src.com", "User", "Sim", true);
        var line = JsonSerializer.Serialize(descriptor, s_jsonOptions);

        var storeMock = new Mock<IArtefactStore>(MockBehavior.Loose);
        storeMock
            .Setup(s => s.ExistsAsync("Identities/descriptors.jsonl", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        storeMock
            .Setup(s => s.ReadAsync("Identities/descriptors.jsonl", It.IsAny<CancellationToken>()))
            .ReturnsAsync(line + "\n");

        var module = CreateModule();
        var context = CreateValidationContext(storeMock);

        // Act
        await module.ValidateAsync(context, CancellationToken.None);

        // Assert
        Assert.AreEqual(0, context.Errors.Count);
    }

    // ── T024g: Prepare/Mapping resilience tests ───────────────────────────────

    [TestMethod]
    [TestCategory("UnitTest")]
    public async Task ImportAsync_CompletesWithoutThrowing_WhenMappingJsonAbsent()
    {
        // Arrange — descriptors present, mapping.json missing
        var descriptor = new IdentityDescriptor("desc-1", "Alice", "alice@src.com", "User", "Sim", true);
        var line = JsonSerializer.Serialize(descriptor, s_jsonOptions);

        var storeMock = new Mock<IArtefactStore>(MockBehavior.Loose);
        storeMock
            .Setup(s => s.ReadAsync("Identities/descriptors.jsonl", It.IsAny<CancellationToken>()))
            .ReturnsAsync(line + "\n");
        storeMock
            .Setup(s => s.ReadAsync("Identities/mapping.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);  // mapping absent

        var module = CreateModule();
        var context = CreateImportContext(storeMock);

        // Act — must not throw; missing mapping is a warning, not an error
        await module.ImportAsync(context, CancellationToken.None);
    }

    // TODO: [test-validity] Score 14/25 — No assertions beyond "no exception thrown". Rewrite to test:
    // assert that the identity mapping service has zero entries loaded when both files are absent (first-run state
    // is verifiably empty), or assert that IProgressSink emits a structured warning when descriptors are missing.
    [TestMethod]
    [TestCategory("UnitTest")]
    public async Task ImportAsync_CompletesWithoutThrowing_WhenBothDescriptorsAndMappingAbsent()
    {
        // Arrange — store returns null for everything
        var storeMock = new Mock<IArtefactStore>(MockBehavior.Loose);
        storeMock
            .Setup(s => s.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var module = CreateModule();
        var context = CreateImportContext(storeMock);

        // Act — module must tolerate empty state (e.g. first run, prepare-only scenario)
        await module.ImportAsync(context, CancellationToken.None);
    }

    // TODO: [test-validity] Score 12/25 — No assertions beyond "no exception thrown". Rewrite to test:
    // after ImportAsync runs with a mapping.json present, assert that IIdentityMappingService.Resolve("desc-1")
    // returns "alice@target.com" (verify the mapping was actually applied, not just loaded without error).
    [TestMethod]
    [TestCategory("UnitTest")]
    public async Task ImportAsync_AppliesMapping_WhenBothDescriptorsAndMappingPresent()
    {
        // Arrange
        var descriptor = new IdentityDescriptor("desc-1", "Alice", "alice@src.com", "User", "Sim", true);
        var descriptorLine = JsonSerializer.Serialize(descriptor, s_jsonOptions);

        // Mapping: source descriptor → target identity
        var mappingJson = JsonSerializer.Serialize(
            new Dictionary<string, string> { ["desc-1"] = "alice@target.com" },
            s_jsonOptions);

        var storeMock = new Mock<IArtefactStore>(MockBehavior.Loose);
        storeMock
            .Setup(s => s.ReadAsync("Identities/descriptors.jsonl", It.IsAny<CancellationToken>()))
            .ReturnsAsync(descriptorLine + "\n");
        storeMock
            .Setup(s => s.ReadAsync("Identities/mapping.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(mappingJson);

        var module = CreateModule();
        var context = CreateImportContext(storeMock);

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
