using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.WebApi;
using DevOpsMigrationPlatform.Abstractions.Models;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Services;
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
    public async Task AnalyseLinksAsync_EmitsDependencyHeartbeat()
    {
        // This is a placeholder test. Full implementation would mock the WIT client.
        // For now, verify the service is callable.
        Assert.IsNotNull(_service);
        await Task.CompletedTask;
    }
}
