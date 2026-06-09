// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure;
using DevOpsMigrationPlatform.Infrastructure.Agent.Identity;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using DevOpsMigrationPlatform.Infrastructure.Agent.Teams;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.IdentityTranslation;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.WorkItems.Revisions;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.WorkItems.WorkItemResolution;
using DevOpsMigrationPlatform.Infrastructure.Serialization;
using DevOpsMigrationPlatform.Infrastructure.Simulated;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.SchemaGenerator.Tests;

/// <summary>
/// Simple in-memory logger for capturing log calls in unit tests.
/// Avoids Moq generic-type-argument issues with ILogger.
/// </summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = new();

    public bool IsEnabled(LogLevel logLevel) => true;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add((logLevel, formatter(state, exception)));
    }
}

/// <summary>
/// Unit tests for SchemaGeneratorHost — O-1 traces and O-3 logging behaviour.
/// T027: Assert ActivitySource.StartActivity called with span name "schema.generate" and entry_count > 0.
/// T028: Assert LogInformation called at start and success; LogError called on duplicate SectionPath.
/// </summary>
[TestClass]
public sealed class SchemaGeneratorHostTests
{
    private static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSimulatedServices();
        services.AddAzureDevOpsWorkItemExport();
        services.AddAzureDevOpsWorkItem();
        services.AddIdentitiesModule(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        services.AddNodesModule(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        services.AddTeamsModule(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        services.AddWorkItemsModule();
        services.AddFieldTransformToolServices();
        services.AddNodeTranslationToolServices();
        services.AddMigrationPlatformPolymorphicSerializers();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        return services.BuildServiceProvider();
    }

    // ── T027: O-1 Traces ──────────────────────────────────────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task RunAsync_RecordedActivity_HasSchemaGenerateSpanName()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"schema-test-{Guid.NewGuid()}.json");
        try
        {
            var activitiesRecorded = new List<Activity>();
            using var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == WellKnownActivitySourceNames.Migration,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStarted = a => activitiesRecorded.Add(a)
            };
            ActivitySource.AddActivityListener(listener);

            var sp = BuildServiceProvider();
            var logger = sp.GetRequiredService<ILogger<SchemaGeneratorHost>>();
            var host = new SchemaGeneratorHost(sp, logger);

            var result = await host.RunAsync(outputPath, CancellationToken.None);

            Assert.AreEqual(0, result, "RunAsync should return 0 on success");

            var schemaActivity = activitiesRecorded.Find(a => a.OperationName == "schema.generate");
            Assert.IsNotNull(schemaActivity, "Expected an activity named 'schema.generate'");

            var entryCount = schemaActivity.GetTagItem("schema.entry_count");
            Assert.IsNotNull(entryCount, "Expected 'schema.entry_count' tag on the activity");
            Assert.IsTrue(Convert.ToInt32(entryCount) > 0, "schema.entry_count must be > 0");
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    // ── T028: O-3 Logs ────────────────────────────────────────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task RunAsync_OnSuccess_LogsInformationAtStartAndCompletion()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"schema-test-{Guid.NewGuid()}.json");
        try
        {
            var logger = new CapturingLogger<SchemaGeneratorHost>();
            var sp = BuildServiceProvider();
            var host = new SchemaGeneratorHost(sp, logger);

            var result = await host.RunAsync(outputPath, CancellationToken.None);

            Assert.AreEqual(0, result, "RunAsync should return 0 on success");

            var infoMessages = logger.Entries.FindAll(m => m.Level == LogLevel.Information);
            Assert.IsTrue(infoMessages.Count >= 2,
                $"Expected at least 2 Information log entries (start + success). Got {infoMessages.Count}: " +
                string.Join("; ", infoMessages.ConvertAll(m => m.Message)));

            Assert.IsTrue(infoMessages.Exists(m => m.Message.Contains("started")),
                "Expected a 'started' Information log entry");
            Assert.IsTrue(infoMessages.Exists(m => m.Message.Contains("succeeded")),
                "Expected a 'succeeded' Information log entry");
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task RunAsync_DuplicateSectionPath_LogsErrorNotInformation()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"schema-test-{Guid.NewGuid()}.json");
        try
        {
            var logger = new CapturingLogger<SchemaGeneratorHost>();

            // Build a service provider with a deliberate duplicate SectionPath
            var services = new ServiceCollection();
            services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
            // Register WorkItemsModuleOptions twice to trigger duplicate detection
            services.AddSingleton(new SchemaOptionsEntry
            {
                OptionsType = typeof(WorkItemsModuleOptions),
                SectionPath = "MigrationPlatform:Modules:WorkItems",
                Description = "First"
            });
            services.AddSingleton(new SchemaOptionsEntry
            {
                OptionsType = typeof(WorkItemsModuleOptions),
                SectionPath = "MigrationPlatform:Modules:WorkItems",
                Description = "Duplicate"
            });
            var sp = services.BuildServiceProvider();
            var host = new SchemaGeneratorHost(sp, logger);

            var result = await host.RunAsync(outputPath, CancellationToken.None);

            Assert.AreNotEqual(0, result, "RunAsync should return non-zero when duplicate SectionPath detected");

            var errorMessages = logger.Entries.FindAll(m => m.Level == LogLevel.Error);
            Assert.IsTrue(errorMessages.Count > 0,
                "Expected at least one Error log entry for duplicate SectionPath");

            Assert.IsFalse(logger.Entries.Exists(m =>
                m.Level == LogLevel.Information && m.Message.Contains("succeeded")),
                "RunAsync must NOT log 'succeeded' when duplicate SectionPath is detected");
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }
}

