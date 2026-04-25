using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.WebApi;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Services;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Dependencies;

[TestClass]
public class AzureDevOpsDependencyAnalysisServiceTests
{
    private Mock<IOptions<DiscoveryOptions>> _optionsMock = null!;
    private Mock<IAzureDevOpsClientFactory> _clientFactoryMock = null!;
    private Mock<IWorkItemFetchService> _fetchServiceMock = null!;
    private Mock<IWorkItemDiscoveryService> _discoveryServiceMock = null!;
    private Mock<ILogger<AzureDevOpsDependencyAnalysisService>> _loggerMock = null!;
    private AzureDevOpsDependencyAnalysisService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _optionsMock = new Mock<IOptions<DiscoveryOptions>>();
        _optionsMock.Setup(o => o.Value).Returns(new DiscoveryOptions { Policies = new() { Throttle = new() { MaxConcurrency = 4 } } });

        _clientFactoryMock = new Mock<IAzureDevOpsClientFactory>();
        _fetchServiceMock = new Mock<IWorkItemFetchService>();
        _discoveryServiceMock = new Mock<IWorkItemDiscoveryService>();
        _loggerMock = new Mock<ILogger<AzureDevOpsDependencyAnalysisService>>();

        _service = new AzureDevOpsDependencyAnalysisService(
            _optionsMock.Object,
            _clientFactoryMock.Object,
            _fetchServiceMock.Object,
            _discoveryServiceMock.Object,
            _loggerMock.Object);
    }

    [TestMethod]
    public void Service_CanBeConstructed()
    {
        Assert.IsNotNull(_service);
    }

    [TestMethod]
    public void Service_ThrowsArgumentNullException_WhenOptionsNull()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            new AzureDevOpsDependencyAnalysisService(
                null!,
                _clientFactoryMock.Object,
                _fetchServiceMock.Object,
                _discoveryServiceMock.Object,
                _loggerMock.Object));
    }

    [TestMethod]
    public void Service_ThrowsArgumentNullException_WhenClientFactoryNull()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            new AzureDevOpsDependencyAnalysisService(
                _optionsMock.Object,
                null!,
                _fetchServiceMock.Object,
                _discoveryServiceMock.Object,
                _loggerMock.Object));
    }

    [TestMethod]
    public void Service_ThrowsArgumentNullException_WhenFetchServiceNull()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            new AzureDevOpsDependencyAnalysisService(
                _optionsMock.Object,
                _clientFactoryMock.Object,
                null!,
                _discoveryServiceMock.Object,
                _loggerMock.Object));
    }

    [TestMethod]
    public void Service_ThrowsArgumentNullException_WhenDiscoveryServiceNull()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            new AzureDevOpsDependencyAnalysisService(
                _optionsMock.Object,
                _clientFactoryMock.Object,
                _fetchServiceMock.Object,
                null!,
                _loggerMock.Object));
    }

    [TestMethod]
    public void Service_ThrowsArgumentNullException_WhenLoggerNull()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            new AzureDevOpsDependencyAnalysisService(
                _optionsMock.Object,
                _clientFactoryMock.Object,
                _fetchServiceMock.Object,
                _discoveryServiceMock.Object,
                null!));
    }

    [TestMethod]
    public async Task AnalyseLinksAsync_WithToken_SetsResumeEnabledTrue()
    {
        // Arrange: supply a continuation token — the service should pass
        // ResumeEnabled=true to IWorkItemFetchService.FetchAsync().
        var endpoint = new AzureDevOpsEndpointOptions
        {
            Url = "https://dev.azure.com/testorg",
            Authentication = new EndpointAuthenticationOptions { AccessToken = "fake" }
        };
        var token = new BatchContinuationToken
        {
            QueryFingerprint = "fp",
            StrategyVersion = "1.0.0",
            WorkItemId = 42,
            ChangedDateUtc = DateTime.UtcNow
        };

        WorkItemFetchScope? capturedScope = null;
        _fetchServiceMock
            .Setup(f => f.FetchAsync(It.IsAny<OrganisationEndpoint>(), "TestProject", It.IsAny<WorkItemFetchScope>(), It.IsAny<CancellationToken>()))
            .Callback<OrganisationEndpoint, string, WorkItemFetchScope, CancellationToken>((_, _, scope, _) => capturedScope = scope)
            .Returns(AsyncEnumerable.Empty<FetchedWorkItem>());

        _discoveryServiceMock
            .Setup(d => d.CountWorkItemsAsync(It.IsAny<OrganisationEndpoint>(), "TestProject", null, It.IsAny<CancellationToken>()))
            .Returns(AsyncEnumerable.Empty<ProjectDiscoverySummary>());

        // Act: drain the stream
        await foreach (var _ in _service.AnalyseLinksAsync(endpoint, "TestProject", savedContinuationToken: token)) { }

        // Assert
        Assert.IsNotNull(capturedScope, "FetchAsync should have been called with a scope.");
        Assert.IsTrue(capturedScope!.ResumeEnabled, "ResumeEnabled should be true when a token is supplied.");
        Assert.AreSame(token, capturedScope.SavedContinuationToken, "The exact token should be passed through.");
    }

    [TestMethod]
    public async Task AnalyseLinksAsync_WithNoToken_SetsResumeEnabledFalse()
    {
        // Arrange: no token — the service should default to ResumeEnabled=false.
        var endpoint = new AzureDevOpsEndpointOptions
        {
            Url = "https://dev.azure.com/testorg",
            Authentication = new EndpointAuthenticationOptions { AccessToken = "fake" }
        };

        WorkItemFetchScope? capturedScope = null;
        _fetchServiceMock
            .Setup(f => f.FetchAsync(It.IsAny<OrganisationEndpoint>(), "TestProject", It.IsAny<WorkItemFetchScope>(), It.IsAny<CancellationToken>()))
            .Callback<OrganisationEndpoint, string, WorkItemFetchScope, CancellationToken>((_, _, scope, _) => capturedScope = scope)
            .Returns(AsyncEnumerable.Empty<FetchedWorkItem>());

        _discoveryServiceMock
            .Setup(d => d.CountWorkItemsAsync(It.IsAny<OrganisationEndpoint>(), "TestProject", null, It.IsAny<CancellationToken>()))
            .Returns(AsyncEnumerable.Empty<ProjectDiscoverySummary>());

        // Act
        await foreach (var _ in _service.AnalyseLinksAsync(endpoint, "TestProject")) { }

        // Assert
        Assert.IsNotNull(capturedScope, "FetchAsync should have been called with a scope.");
        Assert.IsFalse(capturedScope!.ResumeEnabled, "ResumeEnabled should be false when no token is supplied.");
        Assert.IsNull(capturedScope.SavedContinuationToken, "No token should be present in scope.");
    }
}
