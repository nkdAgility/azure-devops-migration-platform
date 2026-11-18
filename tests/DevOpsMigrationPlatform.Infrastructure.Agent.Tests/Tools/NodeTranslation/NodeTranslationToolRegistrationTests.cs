// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.NodeTranslation;

/// <summary>
/// #171 live run: the TFS agent registers its own tree capture before the node translation
/// tools; the generic capture replaced it and needed a tree source the TFS agent does not have,
/// so every TFS Export failed to start.
/// </summary>
[TestClass]
public class NodeTranslationToolRegistrationTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void AddNodeTranslationToolServices_ConnectorCaptureAlreadyRegistered_KeepsTheConnectorCapture()
    {
        var connectorCapture = new Mock<IClassificationTreeCapture>().Object;
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddSingleton(connectorCapture);

        services.AddNodeTranslationToolServices();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.AreSame(connectorCapture, scope.ServiceProvider.GetRequiredService<IClassificationTreeCapture>());
    }
}
