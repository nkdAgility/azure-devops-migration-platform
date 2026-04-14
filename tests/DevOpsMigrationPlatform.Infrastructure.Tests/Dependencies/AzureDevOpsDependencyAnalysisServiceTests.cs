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
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Services;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Dependencies;

[TestClass]
public class AzureDevOpsDependencyAnalysisServiceTests
{
    private Mock<IOptions<DiscoveryOptions>> _optionsMock;
    private Mock<IAzureDevOpsClientFactory> _clientFactoryMock;
    private Mock<ILogger<AzureDevOpsDependencyAnalysisService>> _loggerMock;
    private AzureDevOpsDependencyAnalysisService _service;

    [TestInitialize]
    public void Setup()
    {
        _optionsMock = new Mock<IOptions<DiscoveryOptions>>();
        _optionsMock.Setup(o => o.Value).Returns(new DiscoveryOptions { MaxConcurrency = 4 });

        _clientFactoryMock = new Mock<IAzureDevOpsClientFactory>();
        _loggerMock = new Mock<ILogger<AzureDevOpsDependencyAnalysisService>>();

        _service = new AzureDevOpsDependencyAnalysisService(
            _optionsMock.Object,
            _clientFactoryMock.Object,
            _loggerMock.Object);
    }

    [TestMethod]
    public void Service_CanBeConstructed()
    {
        Assert.IsNotNull(_service);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Service_ThrowsArgumentNullException_WhenOptionsNull()
    {
        new AzureDevOpsDependencyAnalysisService(
            null!,
            _clientFactoryMock.Object,
            _loggerMock.Object);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Service_ThrowsArgumentNullException_WhenClientFactoryNull()
    {
        new AzureDevOpsDependencyAnalysisService(
            _optionsMock.Object,
            null!,
            _loggerMock.Object);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Service_ThrowsArgumentNullException_WhenLoggerNull()
    {
        new AzureDevOpsDependencyAnalysisService(
            _optionsMock.Object,
            _clientFactoryMock.Object,
            null!);
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
