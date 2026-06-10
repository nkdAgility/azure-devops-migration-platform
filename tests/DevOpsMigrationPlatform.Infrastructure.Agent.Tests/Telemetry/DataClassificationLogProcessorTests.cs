// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if !NETFRAMEWORK
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using OpenTelemetry.Logs;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void OnEnd_UnclassifiedLog_PassesThrough()
    {
        _logger.LogInformation("System startup complete");
        _factory.Dispose(); // flush

        Assert.AreEqual(1, _exported.Count);
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
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

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
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

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
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

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
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

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
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

    /// <summary>
    /// Verifies that a Customer-classified log written inside a data scope
    /// is captured by the PackageLogger (package log file) with the correct
    /// DataClassification tag, even though the OTel pipeline filters it out.
    /// Covers: "Customer-classified log still appears in the package log file".
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void PackageLogger_CustomerClassifiedLog_IsPresentWithClassificationTag()
    {
        // Arrange — create a PackageLoggerProvider backed by an in-memory list.
        var captured = new List<DiagnosticLogRecord>();
        var mockPackage = new Mock<IPackageAccess>(MockBehavior.Strict);
        mockPackage.Setup(p => p.AppendLogAsync(
                It.IsAny<PackageLogContext>(),
                It.IsAny<PackageLogPayload>(),
                It.IsAny<CancellationToken>()))
            .Callback<PackageLogContext, PackageLogPayload, CancellationToken>((_, payload, _) =>
            {
                payload.Content.Position = 0;
                using var reader = new StreamReader(payload.Content, Encoding.UTF8, leaveOpen: true);
                var ndjson = reader.ReadToEnd();
                foreach (var line in ndjson.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var record = JsonSerializer.Deserialize<DiagnosticLogRecord>(line);
                    if (record is not null)
                        captured.Add(record);
                }
            })
            .Returns(ValueTask.CompletedTask);

        var packageState = new ActivePackageState
        {
            CurrentJob = new Job { JobId = "data-classification-test", Kind = JobKind.Export }
        };
        var opts = Options.Create(new DiagnosticLogOptions());
        var services = new ServiceCollection();
        services.AddSingleton(mockPackage.Object);
        var sp = services.BuildServiceProvider();

        using var packageProvider = new PackageLoggerProvider(packageState, opts, sp);
        var packageLogger = packageProvider.CreateLogger("DataClassificationTest");

        // Act — write a log inside a Customer data classification scope.
        using (packageLogger.BeginDataScope(DataClassification.Customer))
        {
            packageLogger.LogInformation("Processing work item 12345");
        }

        // Flush synchronously.
        packageProvider.FlushAsync().GetAwaiter().GetResult();

        // Assert — the record is present in the package log with Customer classification.
        Assert.AreEqual(1, captured.Count, "Customer-classified log must appear in the package log file.");
        Assert.AreEqual(DataClassification.Customer.ToString(), captured[0].DataClassification,
            "The DataClassification tag must be Customer.");
    }
}
#endif
