// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Identity;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Import.Validators;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Import;

[TestClass]
public sealed class IdentityMappingValidatorTests
{
    [TestMethod]
    public async Task EvaluateAsync_ReturnsWarningFindings_WhenIdentityMappingServiceLeavesEntriesUnresolved()
    {
        const string descriptorsPath = "Identities/descriptors.jsonl";
        const string mappingPath = "Identities/mapping.json";

        var package = PackageTestFactory.CreateLooseMock();
        package
            .Setup(p => p.RequestContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext context, CancellationToken _) =>
            {
                var path = context.Address!.RelativePath.Replace('\\', '/');
                return path switch
                {
                    descriptorsPath => ValueTask.FromResult<PackagePayload?>(CreateTextPayload(
                        Serialize(new IdentityDescriptor("desc-1", "Alice", "alice@source.example", "User", "AzureDevOps", true)) + "\n" +
                        Serialize(new IdentityDescriptor("desc-2", "Bob", "bob@source.example", "User", "AzureDevOps", true)) + "\n")),
                    mappingPath => ValueTask.FromResult<PackagePayload?>(CreateTextPayload("{\"alice@source.example\":\"alice@target.example\"}")),
                    _ => ValueTask.FromResult<PackagePayload?>(null)
                };
            });

        var identityMappingService = new Mock<IIdentityMappingService>(MockBehavior.Strict);
        identityMappingService.Setup(s => s.LoadMappingOverrides(It.IsAny<string?>()));
        identityMappingService.Setup(s => s.Resolve("alice@source.example")).Returns("alice@target.example");
        identityMappingService.Setup(s => s.Resolve("bob@source.example")).Returns("bob@source.example");

        var sut = new IdentityMappingValidator(identityMappingService.Object);

        var findings = await sut.EvaluateAsync(
            new ImportFailurePatternContext(CreatePrepareContext(package.Object), new WorkItemsModuleOptions()),
            CancellationToken.None);

        Assert.AreEqual(1, findings.Count);
        Assert.AreEqual(IdentityMappingValidator.Code, findings[0].PatternCode);
        Assert.AreEqual(ImportFailureSeverity.Warning, findings[0].Severity);
        Assert.AreEqual("bob@source.example", findings[0].EvidenceKey);
        identityMappingService.Verify(s => s.LoadMappingOverrides(It.IsAny<string?>()), Times.Once);
        identityMappingService.Verify(s => s.Resolve("alice@source.example"), Times.Once);
        identityMappingService.Verify(s => s.Resolve("bob@source.example"), Times.Once);
    }

    [TestMethod]
    public async Task EvaluateAsync_ReportsUnresolved_WhenIdentityFallsBackToDefaultIdentityWithoutExplicitMapping()
    {
        const string descriptorsPath = "Identities/descriptors.jsonl";
        const string mappingPath = "Identities/mapping.json";

        var package = PackageTestFactory.CreateLooseMock();
        package
            .Setup(p => p.RequestContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext context, CancellationToken _) =>
            {
                var path = context.Address!.RelativePath.Replace('\\', '/');
                return path switch
                {
                    descriptorsPath => ValueTask.FromResult<PackagePayload?>(CreateTextPayload(
                        Serialize(new IdentityDescriptor("desc-1", "Fallback User", "fallback@source.example", "User", "AzureDevOps", true)) + "\n")),
                    mappingPath => ValueTask.FromResult<PackagePayload?>(CreateTextPayload("{}")),
                    _ => ValueTask.FromResult<PackagePayload?>(null)
                };
            });

        var identityMappingService = new Mock<IIdentityMappingService>(MockBehavior.Strict);
        identityMappingService.Setup(s => s.LoadMappingOverrides(It.IsAny<string?>()));
        identityMappingService.Setup(s => s.Resolve("fallback@source.example")).Returns("migration-default@target.example");

        var sut = new IdentityMappingValidator(identityMappingService.Object);

        var findings = await sut.EvaluateAsync(
            new ImportFailurePatternContext(CreatePrepareContext(package.Object), new WorkItemsModuleOptions()),
            CancellationToken.None);

        Assert.AreEqual(1, findings.Count);
        Assert.AreEqual("fallback@source.example", findings[0].EvidenceKey);
        Assert.AreEqual(ImportFailureSeverity.Warning, findings[0].Severity);
        identityMappingService.Verify(s => s.Resolve("fallback@source.example"), Times.Once);
    }

    [TestMethod]
    public async Task EvaluateAsync_DoesNotReportUnresolved_WhenIdentityHasExplicitSelfMapping()
    {
        const string descriptorsPath = "Identities/descriptors.jsonl";
        const string mappingPath = "Identities/mapping.json";

        var package = PackageTestFactory.CreateLooseMock();
        package
            .Setup(p => p.RequestContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext context, CancellationToken _) =>
            {
                var path = context.Address!.RelativePath.Replace('\\', '/');
                return path switch
                {
                    descriptorsPath => ValueTask.FromResult<PackagePayload?>(CreateTextPayload(
                        Serialize(new IdentityDescriptor("desc-1", "Self Map User", "self@source.example", "User", "AzureDevOps", true)) + "\n")),
                    mappingPath => ValueTask.FromResult<PackagePayload?>(CreateTextPayload("{\"self@source.example\":\"self@source.example\"}")),
                    _ => ValueTask.FromResult<PackagePayload?>(null)
                };
            });

        var identityMappingService = new Mock<IIdentityMappingService>(MockBehavior.Strict);
        identityMappingService.Setup(s => s.LoadMappingOverrides(It.IsAny<string?>()));
        identityMappingService.Setup(s => s.Resolve("self@source.example")).Returns("self@source.example");

        var sut = new IdentityMappingValidator(identityMappingService.Object);

        var findings = await sut.EvaluateAsync(
            new ImportFailurePatternContext(CreatePrepareContext(package.Object), new WorkItemsModuleOptions()),
            CancellationToken.None);

        Assert.AreEqual(0, findings.Count);
        identityMappingService.Verify(s => s.Resolve("self@source.example"), Times.Once);
    }

    [TestMethod]
    public async Task EvaluateAsync_ReturnsSortedWarningFindings_WhenMultipleIdentitiesAreUnresolved()
    {
        const string descriptorsPath = "Identities/descriptors.jsonl";
        const string mappingPath = "Identities/mapping.json";

        var package = PackageTestFactory.CreateLooseMock();
        package
            .Setup(p => p.RequestContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext context, CancellationToken _) =>
            {
                var path = context.Address!.RelativePath.Replace('\\', '/');
                return path switch
                {
                    descriptorsPath => ValueTask.FromResult<PackagePayload?>(CreateTextPayload(
                        Serialize(new IdentityDescriptor("desc-1", "Zulu User", "zulu@source.example", "User", "AzureDevOps", true)) + "\n" +
                        Serialize(new IdentityDescriptor("desc-2", "Alpha User", "alpha@source.example", "User", "AzureDevOps", true)) + "\n")),
                    mappingPath => ValueTask.FromResult<PackagePayload?>(CreateTextPayload("{}")),
                    _ => ValueTask.FromResult<PackagePayload?>(null)
                };
            });

        var identityMappingService = new Mock<IIdentityMappingService>(MockBehavior.Strict);
        identityMappingService.Setup(s => s.LoadMappingOverrides(It.IsAny<string?>()));
        identityMappingService.Setup(s => s.Resolve("zulu@source.example")).Returns("migration-default@target.example");
        identityMappingService.Setup(s => s.Resolve("alpha@source.example")).Returns("migration-default@target.example");

        var sut = new IdentityMappingValidator(identityMappingService.Object);

        var findings = await sut.EvaluateAsync(
            new ImportFailurePatternContext(CreatePrepareContext(package.Object), new WorkItemsModuleOptions()),
            CancellationToken.None);

        Assert.AreEqual(2, findings.Count);
        Assert.AreEqual("alpha@source.example", findings[0].EvidenceKey);
        Assert.AreEqual("zulu@source.example", findings[1].EvidenceKey);
        Assert.AreEqual(ImportFailureSeverity.Warning, findings[0].Severity);
        Assert.AreEqual(ImportFailureSeverity.Warning, findings[1].Severity);
    }

    [TestMethod]
    public async Task EvaluateAsync_ReturnsEmpty_WhenDescriptorArtefactIsMissing()
    {
        var package = PackageTestFactory.CreateLooseMock();
        package
            .Setup(p => p.RequestContentAsync(It.IsAny<PackageContentContext>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.FromResult<PackagePayload?>(null));

        var identityMappingService = new Mock<IIdentityMappingService>(MockBehavior.Strict);
        identityMappingService.Setup(s => s.LoadMappingOverrides(It.IsAny<string?>()));

        var sut = new IdentityMappingValidator(identityMappingService.Object);

        var findings = await sut.EvaluateAsync(
            new ImportFailurePatternContext(CreatePrepareContext(package.Object), new WorkItemsModuleOptions()),
            CancellationToken.None);

        Assert.AreEqual(0, findings.Count);
        identityMappingService.Verify(s => s.LoadMappingOverrides(It.IsAny<string?>()), Times.Once);
        identityMappingService.Verify(s => s.Resolve(It.IsAny<string>()), Times.Never);
    }

    private static string Serialize(IdentityDescriptor descriptor)
        => JsonSerializer.Serialize(descriptor);

    private static PackagePayload CreateTextPayload(string content)
        => new(new MemoryStream(Encoding.UTF8.GetBytes(content)));

    private static PrepareContext CreatePrepareContext(IPackageAccess package)
    {
        var targetEndpoint = new Mock<ITargetEndpointInfo>();
        targetEndpoint.SetupGet(s => s.Project).Returns("ProjectA");
        targetEndpoint.SetupGet(s => s.Url).Returns("https://target.example");
        targetEndpoint.SetupGet(s => s.ConnectorType).Returns("Simulated");

        return new PrepareContext
        {
            Job = new Job { JobId = "job-prepare-identities", Kind = JobKind.Prepare },
            Package = package,
            ProgressSink = Mock.Of<IProgressSink>(),
            TargetEndpoint = targetEndpoint.Object
        };
    }
}
