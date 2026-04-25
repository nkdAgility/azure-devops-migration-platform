#if !NETFRAMEWORK
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTelemetry.Logs;
using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Telemetry;

[TestClass]
public class DataClassificationLogProcessorTests
{
    private List<LogRecord> _exported = null!;
    private ILoggerFactory _factory = null!;
    private ILogger _logger = null!;

    [TestInitialize]
    public void Setup()
    {
        _exported = new List<LogRecord>();
        _factory = LoggerFactory.Create(builder =>
        {
            builder.AddDataClassificationFilter();
            builder.AddOpenTelemetry(otel =>
            {
                otel.IncludeScopes = true;
                otel.AddInMemoryExporter(_exported);
            });
        });
        _logger = _factory.CreateLogger("Test");
    }

    [TestCleanup]
    public void Cleanup() => _factory.Dispose();

    [TestMethod]
    public void OnEnd_UnclassifiedLog_PassesThrough()
    {
        _logger.LogInformation("System startup complete");
        _factory.Dispose(); // flush

        Assert.AreEqual(1, _exported.Count);
    }

    [TestMethod]
    public void OnEnd_CustomerClassifiedLog_IsFiltered()
    {
        using (_logger.BeginDataScope(DataClassification.Customer))
        {
            _logger.LogInformation("Processing work item 12345");
        }
        _factory.Dispose();

        Assert.AreEqual(0, _exported.Count);
    }

    [TestMethod]
    public void OnEnd_DerivedClassifiedLog_PassesThrough()
    {
        using (_logger.BeginDataScope(DataClassification.Derived))
        {
            _logger.LogInformation("Processed 500 work items");
        }
        _factory.Dispose();

        Assert.AreEqual(1, _exported.Count);
    }

    [TestMethod]
    public void OnEnd_SystemClassifiedLog_PassesThrough()
    {
        using (_logger.BeginDataScope(DataClassification.System))
        {
            _logger.LogInformation("Module lifecycle event");
        }
        _factory.Dispose();

        Assert.AreEqual(1, _exported.Count);
    }

    [TestMethod]
    public void OnEnd_NestedInnerCustomerInOuterSystem_IsFiltered()
    {
        using (_logger.BeginDataScope(DataClassification.System))
        {
            using (_logger.BeginDataScope(DataClassification.Customer))
            {
                _logger.LogInformation("Work item 42 field System.Title");
            }
        }
        _factory.Dispose();

        Assert.AreEqual(0, _exported.Count);
    }

    [TestMethod]
    public void OnEnd_NestedInnerSystemInOuterCustomer_PassesThrough()
    {
        using (_logger.BeginDataScope(DataClassification.Customer))
        {
            using (_logger.BeginDataScope(DataClassification.System))
            {
                _logger.LogInformation("Error connecting to endpoint");
            }
        }
        _factory.Dispose();

        Assert.AreEqual(1, _exported.Count);
    }
}
#endif
