using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Agent.Identity;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

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
        IIdentitySource? identitySource = null)
    {
        options ??= new IdentitiesModuleOptions { Enabled = true };
        return new IdentitiesModule(
            NullLogger<IdentitiesModule>.Instance,
            Options.Create(options),
            identitySource);
    }

    private static ExportContext CreateExportContext(Mock<IArtefactStore> store)
    {
        return new ExportContext
        {
            Job = new MigrationJob { Mode = "Export", Source = new SimulatedEndpointOptions() },
            ArtefactStore = store.Object,
            StateStore = Mock.Of<IStateStore>(),
            ProgressSink = Mock.Of<IProgressSink>()
        };
    }

    private static ImportContext CreateImportContext(Mock<IArtefactStore> store)
    {
        return new ImportContext
        {
            Job = new MigrationJob { Mode = "Import" },
            ArtefactStore = store.Object,
            StateStore = Mock.Of<IStateStore>(),
            ProgressSink = Mock.Of<IProgressSink>()
        };
    }

    private static ValidationContext CreateValidationContext(Mock<IArtefactStore> store)
    {
        return new ValidationContext
        {
            Job = new MigrationJob(),
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

        var module = CreateModule(identitySource: source);
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
