// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Architecture;

/// <summary>
/// Contract compatibility tests for ADR-0023 (CA-C1, CA-H1/HX-M1, VS-H1, VS-H2, VS-H3, VS-M3):
/// hidden seams shared across slices/modules are promoted to canonical ports in the
/// Abstractions(.Agent) inner ring; workers and slices depend on the ports, not on
/// concrete infrastructure types or static helpers owned by another slice.
/// Uses reflection/file checks so the assertions are observable RED before the promotion.
/// </summary>
[TestClass]
public sealed class AbstractionsPortArchitectureTests
{
    private static Assembly AbstractionsAssembly =>
        typeof(DevOpsMigrationPlatform.Abstractions.ControlPlaneApi.JobTaskList).Assembly;

    private static Assembly AbstractionsAgentAssembly =>
        typeof(DevOpsMigrationPlatform.Abstractions.Agent.Modules.IModule).Assembly;

    private static Assembly InfrastructureAgentAssembly =>
        typeof(DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry.UnifiedWorkerEventWriter).Assembly;

    private static Assembly MigrationAgentAssembly =>
        typeof(DevOpsMigrationPlatform.MigrationAgent.JobAgentWorker).Assembly;

    // ── CA-C1: IWorkerEventWriter port ───────────────────────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void WorkerEventWriterPort_IsDefinedInAbstractionsAgent()
    {
        var port = AbstractionsAgentAssembly.GetType(
            "DevOpsMigrationPlatform.Abstractions.Agent.Telemetry.IWorkerEventWriter");
        Assert.IsNotNull(port,
            "IWorkerEventWriter must be a canonical port in Abstractions.Agent (CA-C1 / ADR-0023).");
        Assert.IsTrue(port!.IsInterface);
        Assert.IsNotNull(port.GetMethod("EnqueueTasks"), "IWorkerEventWriter must expose EnqueueTasks.");
        Assert.IsNotNull(port.GetMethod("EnqueueTerminal"), "IWorkerEventWriter must expose EnqueueTerminal.");
        Assert.IsNotNull(port.GetMethod("FlushAsync"), "IWorkerEventWriter must expose FlushAsync.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void UnifiedWorkerEventWriter_ImplementsWorkerEventWriterPort()
    {
        var writer = typeof(DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry.UnifiedWorkerEventWriter);
        Assert.IsTrue(
            writer.GetInterfaces().Any(i => i.FullName ==
                "DevOpsMigrationPlatform.Abstractions.Agent.Telemetry.IWorkerEventWriter"),
            "UnifiedWorkerEventWriter must implement the IWorkerEventWriter port (CA-C1 / ADR-0023).");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Workers_DoNotInjectConcreteUnifiedWorkerEventWriter()
    {
        // CA-C1: workers depend on the IWorkerEventWriter port, never the concrete writer.
        var workerTypes = new[]
        {
            MigrationAgentAssembly.GetType("DevOpsMigrationPlatform.MigrationAgent.JobAgentWorker"),
            InfrastructureAgentAssembly.GetType("DevOpsMigrationPlatform.Infrastructure.Agent.AgentWorkerBase"),
            InfrastructureAgentAssembly.GetType("DevOpsMigrationPlatform.Infrastructure.Agent.ModulePipelineWorkerBase"),
        };

        foreach (var workerType in workerTypes)
        {
            Assert.IsNotNull(workerType);
            var offending = workerType!
                .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .SelectMany(c => c.GetParameters())
                .Where(p => p.ParameterType.Name == "UnifiedWorkerEventWriter")
                .ToList();
            Assert.AreEqual(0, offending.Count,
                $"{workerType.Name} must inject IWorkerEventWriter, not the concrete UnifiedWorkerEventWriter (CA-C1 / ADR-0023).");
        }

        // TfsJobAgentWorker is net481-only — assert on source instead of reflection.
        var tfsWorker = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "DevOpsMigrationPlatform.TfsMigrationAgent", "TfsJobAgentWorker.cs"));
        Assert.IsFalse(
            tfsWorker.Contains("UnifiedWorkerEventWriter "),
            "TfsJobAgentWorker must inject IWorkerEventWriter, not the concrete UnifiedWorkerEventWriter (CA-C1 / ADR-0023).");
    }

    // ── CA-H1 / HX-M1: ITfsJobServiceFactory port ────────────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void TfsJobServiceFactoryPort_IsDefinedInAbstractionsAgent()
    {
        var factory = AbstractionsAgentAssembly.GetTypes()
            .FirstOrDefault(t => t.Name == "ITfsJobServiceFactory");
        Assert.IsNotNull(factory,
            "ITfsJobServiceFactory must be a canonical port in Abstractions.Agent (CA-H1/HX-M1 / ADR-0023).");
        Assert.IsTrue(factory!.IsInterface);

        var services = AbstractionsAgentAssembly.GetTypes()
            .FirstOrDefault(t => t.Name == "ITfsJobServices");
        Assert.IsNotNull(services,
            "ITfsJobServices must accompany the factory port so no TFS SDK types leak (CA-H1 / ADR-0023).");
        Assert.IsTrue(typeof(IDisposable).IsAssignableFrom(services!));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void TfsJobServiceFactoryInterface_NoLongerLivesInTfsObjectModelModule()
    {
        var oldPath = Path.Combine(
            FindRepoRoot(), "src", "DevOpsMigrationPlatform.Infrastructure.TfsObjectModel",
            "JobLifecycle", "TfsExecution", "ITfsJobServiceFactory.cs");
        Assert.IsFalse(File.Exists(oldPath),
            "ITfsJobServiceFactory must not be declared inside Infrastructure.TfsObjectModel (CA-H1/HX-M1 / ADR-0023).");
    }

    // ── VS-H1: IWorkItemRevisionReader port ──────────────────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void WorkItemRevisionReaderPort_IsDefinedInAbstractionsAgent_AndImplemented()
    {
        var port = AbstractionsAgentAssembly.GetType(
            "DevOpsMigrationPlatform.Abstractions.Agent.WorkItems.IWorkItemRevisionReader");
        Assert.IsNotNull(port,
            "IWorkItemRevisionReader must be a canonical port in Abstractions.Agent (VS-H1 / ADR-0023).");

        var parsed = AbstractionsAgentAssembly.GetType(
            "DevOpsMigrationPlatform.Abstractions.Agent.WorkItems.ParsedWorkItemRevision");
        Assert.IsNotNull(parsed,
            "ParsedWorkItemRevision must live beside the port in Abstractions.Agent (VS-H1 / ADR-0023).");

        var impl = InfrastructureAgentAssembly.GetType(
            "DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Revisions.WorkItemsPrepareRevisionReader");
        Assert.IsNotNull(impl);
        Assert.IsFalse(impl!.IsAbstract && impl.IsSealed,
            "WorkItemsPrepareRevisionReader must be an injectable instance implementation, not a static helper (VS-H1 / ADR-0023).");
        Assert.IsTrue(port!.IsAssignableFrom(impl),
            "WorkItemsPrepareRevisionReader must implement IWorkItemRevisionReader (VS-H1 / ADR-0023).");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void RevisionReaderConsumers_DoNotCallStaticHelper()
    {
        var repoRoot = FindRepoRoot();
        var consumers = new[]
        {
            Path.Combine("WorkItems", "Revisions", "ImportFailures", "MissingRevisionArtefactImportFailurePattern.cs"),
            Path.Combine("WorkItems", "Revisions", "ImportFailures", "InvalidRevisionPayloadImportFailurePattern.cs"),
            Path.Combine("WorkItems", "Revisions", "ImportFailures", "FieldTransformCompatibilityImportFailurePattern.cs"),
            Path.Combine("WorkItems", "Attachments", "ImportFailures", "MissingAttachmentBinaryImportFailurePattern.cs"),
            Path.Combine("WorkItems", "Attachments", "ImportFailures", "MissingEmbeddedImageBinaryImportFailurePattern.cs"),
            Path.Combine("WorkItems", "Nodes", "NodePathValidator.cs"),
            Path.Combine("WorkItems", "WorkItemType", "WorkItemTypeValidator.cs"),
        };

        foreach (var relative in consumers)
        {
            var file = Path.Combine(repoRoot, "src", "DevOpsMigrationPlatform.Infrastructure.Agent", relative);
            Assert.IsTrue(File.Exists(file), $"Expected consumer source at {file}.");
            Assert.IsFalse(
                File.ReadAllText(file).Contains("WorkItemsPrepareRevisionReader.EnumerateAsync("),
                $"{Path.GetFileName(file)} must consume the injected IWorkItemRevisionReader, not the static helper (VS-H1 / ADR-0023).");
        }
    }

    // ── VS-H2: IProjectInventoryReader / IProjectInventoryWriter ports ──────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ProjectInventoryPorts_AreDefinedInAbstractionsAgent_AndImplemented()
    {
        var reader = AbstractionsAgentAssembly.GetType(
            "DevOpsMigrationPlatform.Abstractions.Agent.Discovery.IProjectInventoryReader");
        var writer = AbstractionsAgentAssembly.GetType(
            "DevOpsMigrationPlatform.Abstractions.Agent.Discovery.IProjectInventoryWriter");
        var data = AbstractionsAgentAssembly.GetType(
            "DevOpsMigrationPlatform.Abstractions.Agent.Discovery.ProjectInventoryData");
        Assert.IsNotNull(reader, "IProjectInventoryReader must be a canonical port in Abstractions.Agent (VS-H2 / ADR-0023).");
        Assert.IsNotNull(writer, "IProjectInventoryWriter must be a canonical port in Abstractions.Agent (VS-H2 / ADR-0023).");
        Assert.IsNotNull(data, "ProjectInventoryData must live beside the ports in Abstractions.Agent (VS-H2 / ADR-0023).");

        var impl = InfrastructureAgentAssembly.GetType(
            "DevOpsMigrationPlatform.Infrastructure.Agent.Discovery.ProjectInventoryFileStore");
        Assert.IsNotNull(impl, "A single DI implementation must exist in Infrastructure.Agent (VS-H2 / ADR-0023).");
        Assert.IsTrue(reader!.IsAssignableFrom(impl!));
        Assert.IsTrue(writer!.IsAssignableFrom(impl!));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ProjectInventoryConsumers_DoNotCallStaticProjectInventoryFile()
    {
        var repoRoot = FindRepoRoot();
        var consumers = new[]
        {
            Path.Combine("Discovery", "InventoryOrchestrator.cs"),
            Path.Combine("Analysis", "InventoryAnalyser.cs"),
            Path.Combine("Context", "JobPlanExecutor.cs"),
            Path.Combine("Modules", "IdentitiesOrchestrator.cs"),
            Path.Combine("Modules", "NodesOrchestrator.cs"),
            Path.Combine("Modules", "TeamsOrchestrator.cs"),
            Path.Combine("WorkItems", "WorkItemResolution", "WorkItemsOrchestrator.cs"),
        };

        foreach (var relative in consumers)
        {
            var file = Path.Combine(repoRoot, "src", "DevOpsMigrationPlatform.Infrastructure.Agent", relative);
            Assert.IsTrue(File.Exists(file), $"Expected consumer source at {file}.");
            var content = File.ReadAllText(file);
            Assert.IsFalse(
                content.Contains("ProjectInventoryFile.MergeAsync(") || content.Contains("ProjectInventoryFile.ReadAsync("),
                $"{Path.GetFileName(file)} must consume the injected inventory ports, not the static ProjectInventoryFile (VS-H2 / ADR-0023).");
        }
    }

    // ── VS-H3: KnownProcessIds in Abstractions ───────────────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void KnownProcessIds_LivesInAbstractions_NotInfrastructureAgent()
    {
        var moved = AbstractionsAssembly.GetType(
            "DevOpsMigrationPlatform.Abstractions.ProjectLifecycle.KnownProcessIds");
        Assert.IsNotNull(moved,
            "KnownProcessIds must live in DevOpsMigrationPlatform.Abstractions so connectors depend only on Abstractions (VS-H3 / ADR-0023).");

        var old = InfrastructureAgentAssembly.GetType(
            "DevOpsMigrationPlatform.Infrastructure.Agent.ProjectLifecycle.KnownProcessIds");
        Assert.IsNull(old,
            "KnownProcessIds must no longer live in Infrastructure.Agent (VS-H3 / ADR-0023).");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void KnownProcessIds_ContractBehaviourIsPreserved()
    {
        var type = AbstractionsAssembly.GetType(
            "DevOpsMigrationPlatform.Abstractions.ProjectLifecycle.KnownProcessIds");
        Assert.IsNotNull(type);

        string Const(string name) => (string)type!.GetField(name)!.GetRawConstantValue()!;
        Assert.AreEqual("adcc42ab-9882-485e-a3ed-7678f01f66bc", Const("Agile"));
        Assert.AreEqual("6b724908-ef14-45cf-84f8-768b5384da45", Const("Scrum"));
        Assert.AreEqual("27450541-8e31-4150-9947-dc59f998fc01", Const("Cmmi"));

        var tryResolve = type!.GetMethod("TryResolve")!;
        object?[] args = ["Microsoft.VSTS.Process.Scrum", null];
        Assert.IsTrue((bool)tryResolve.Invoke(null, args)!);
        Assert.AreEqual(Const("Scrum"), args[1]);

        args = ["not-a-process", null];
        Assert.IsFalse((bool)tryResolve.Invoke(null, args)!);
        Assert.AreEqual(Const("Agile"), args[1], "Unknown processes must fall back to Agile.");
    }

    // ── VS-M3: revision-folder naming contract in Abstractions.Agent ────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void WorkItemRevisionFolderParser_LivesInAbstractionsAgent()
    {
        var parser = AbstractionsAgentAssembly.GetType(
            "DevOpsMigrationPlatform.Abstractions.Agent.WorkItems.WorkItemRevisionFolderParser");
        Assert.IsNotNull(parser,
            "The revision-folder naming contract must live in Abstractions.Agent (VS-M3 / ADR-0023).");

        var old = InfrastructureAgentAssembly.GetType(
            "DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Revisions.WorkItemRevisionFolderParser");
        Assert.IsNull(old,
            "WorkItemRevisionFolderParser must no longer live in Infrastructure.Agent (VS-M3 / ADR-0023).");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void WorkItemRevisionFolderParser_NamingContractIsPreserved()
    {
        var parser = AbstractionsAgentAssembly.GetType(
            "DevOpsMigrationPlatform.Abstractions.Agent.WorkItems.WorkItemRevisionFolderParser");
        Assert.IsNotNull(parser);
        var tryParse = parser!.GetMethod("TryParse")!;

        var parsed = tryParse.Invoke(null, ["638000000000000000-123-4"]);
        Assert.IsNotNull(parsed, "Revision folders {ticks}-{workItemId}-{revisionIndex} must parse.");
        var resultType = parsed!.GetType();
        Assert.AreEqual(638000000000000000L, resultType.GetProperty("Ticks")!.GetValue(parsed));
        Assert.AreEqual(123, resultType.GetProperty("WorkItemId")!.GetValue(parsed));
        Assert.AreEqual(4, resultType.GetProperty("RevisionIndex")!.GetValue(parsed));

        Assert.IsNull(tryParse.Invoke(null, ["638000000000000000-123-c99"]),
            "Comment folders must return null.");
        Assert.IsNull(tryParse.Invoke(null, ["malformed"]), "Malformed names must return null.");
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DevOpsMigrationPlatform.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate repo root walking up from {AppContext.BaseDirectory}.");
    }
}
