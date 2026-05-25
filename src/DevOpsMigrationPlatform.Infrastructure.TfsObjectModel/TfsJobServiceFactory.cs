// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.ProjectLifecycle;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Infrastructure.Agent.ProjectLifecycle;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Attachments;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Discovery;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Export;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Import;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Options;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.ProjectLifecycle;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.Server;
using Microsoft.TeamFoundation.WorkItemTracking.Client;
using Microsoft.TeamFoundation.WorkItemTracking.Proxy;
using Microsoft.VisualStudio.Services.Client;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel;

/// <summary>
/// Per-job TFS service creation. Creates all TFS Object Model services needed for a single
/// export or discovery job — connection, stores, revision source, attachment source, tree reader.
/// Disposable: disposes the <see cref="TfsTeamProjectCollection"/> when the job ends.
///
/// Structural twin of the registrations in <see cref="MigrationPlatformHost.CreateDefaultBuilder"/>
/// but designed for the agent model where the TFS endpoint comes from the job, not from CLI args.
/// </summary>
public sealed class TfsJobServiceFactory : ITfsJobServiceFactory, IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IProjectLifecycleNameGenerator _projectLifecycleNameGenerator;

    public TfsJobServiceFactory(
        ILoggerFactory loggerFactory,
        IProjectLifecycleNameGenerator projectLifecycleNameGenerator)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _projectLifecycleNameGenerator = projectLifecycleNameGenerator ?? throw new ArgumentNullException(nameof(projectLifecycleNameGenerator));
    }

    /// <summary>
    /// Creates a scoped set of TFS services for a single job.
    /// The caller MUST dispose the returned <see cref="TfsJobServices"/> after the job completes.
    /// </summary>
    public TfsJobServices CreateForEndpoint(MigrationEndpointOptions endpoint)
    {
        if (endpoint is not TeamFoundationServerEndpointOptions tfsEndpoint)
            throw new ArgumentException(
                $"Expected TeamFoundationServerEndpointOptions but got {endpoint.GetType().Name}.", nameof(endpoint));

        var serverUrl = new Uri(tfsEndpoint.ResolvedUrl);
        var project = tfsEndpoint.Project;

        // Authenticate — access token or Windows-integrated depending on config.
        //
        // For AccessToken (PAT) endpoints use VssCredentials with ONLY VssBasicCredential and NO
        // WindowsCredential. Including a WindowsCredential triggers NTLM/Kerberos negotiation; on
        // Azure DevOps cloud the server rejects NTLM but the TFS SDK stalls for minutes retrying
        // instead of falling back to Basic auth.  VssCredentials(FederatedCredential) skips all
        // Windows auth challenges and sends the PAT directly.
        //
        // EnsureAuthenticated() is a blocking synchronous call with no cancellation support.
        // Wrap it in a Task with a timeout so a credential failure or unreachable server fails
        // fast rather than blocking the agent for the duration of the job timeout.
        Microsoft.VisualStudio.Services.Common.VssCredentials creds;
        if (tfsEndpoint.Authentication?.Type == AuthenticationType.AccessToken &&
            !string.IsNullOrEmpty(tfsEndpoint.Authentication.ResolvedAccessToken))
        {
            creds = new Microsoft.VisualStudio.Services.Common.VssCredentials(
                new Microsoft.VisualStudio.Services.Common.VssBasicCredential(
                    string.Empty, tfsEndpoint.Authentication.ResolvedAccessToken));
        }
        else
        {
            creds = new VssClientCredentials(true); // Windows-integrated
        }

        var collection = new TfsTeamProjectCollection(serverUrl, creds);

        var authTask = Task.Run(() => collection.EnsureAuthenticated());
        try
        {
            if (!authTask.Wait(TimeSpan.FromSeconds(60)))
            {
                collection.Dispose();
                throw new TimeoutException(
                    $"TFS authentication timed out after 60 s connecting to {serverUrl}. " +
                    "Verify the collection URL and credentials are correct and that the endpoint is reachable.");
            }
        }
        catch (AggregateException ex)
        {
            collection.Dispose();
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(ex.InnerException ?? ex)
                .Throw();
        }

        var workItemStore = new WorkItemStore(collection, WorkItemStoreFlags.BypassRules);

        // Shared attachment registry — links revision enumeration to binary download.
        var attachmentRegistry = new TfsAttachmentRegistry();

        var exportMetrics = new WorkItemExportMetrics();
        var attachmentMetrics = new AttachmentDownloadMetrics();

        var revisionMapper = new TfsWorkItemRevisionMapper(
            exportMetrics,
            _loggerFactory.CreateLogger<TfsWorkItemRevisionMapper>());
        var queryStrategy = new TfsWorkItemQueryWindowStrategy(
            workItemStore,
            _loggerFactory.CreateLogger<TfsWorkItemQueryWindowStrategy>());

        var attachmentDownloader = new TfsAttachmentDownloader(
            workItemStore,
            _loggerFactory.CreateLogger<TfsAttachmentDownloader>(),
            attachmentMetrics);

        var escapedProject = project.Replace("'", "''");
        var wiqlQuery = $"SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = '{escapedProject}'";

        var revisionSource = new TfsWorkItemRevisionSource(
            workItemStore,
            revisionMapper,
            queryStrategy,
            attachmentRegistry,
            project,
            wiqlQuery,
            _loggerFactory.CreateLogger<TfsWorkItemRevisionSource>());

        var attachmentSource = new TfsAttachmentBinarySource(
            attachmentDownloader,
            attachmentRegistry,
            _loggerFactory.CreateLogger<TfsAttachmentBinarySource>());

        var endpointInfo = new SourceEndpointInfo
        {
            Url = tfsEndpoint.ResolvedUrl,
            Project = project,
            ConnectorType = "TeamFoundationServer"
        };

        var classificationTreeReader = new TfsClassificationTreeReader(
            collection,
            _loggerFactory.CreateLogger<TfsClassificationTreeReader>(),
            endpointInfo);
        var commonStructureService = collection.GetService<ICommonStructureService4>();
        var projectUri = commonStructureService.GetProjectFromName(project).Uri;
        var nodeCreator = new TfsNodeCreator(
            commonStructureService,
            _loggerFactory.CreateLogger<TfsNodeCreator>(),
            project,
            projectUri);

        var discoveryService = new TfsObjectModelWorkItemDiscoveryService(
            workItemStore,
            queryStrategy);

        var projectDiscoveryService = new TfsProjectDiscoveryService(workItemStore);

        var fetchService = new TfsWorkItemFetchService(
            workItemStore,
            queryStrategy);

        var identitySource = new TfsIdentitySource(
            collection,
            _loggerFactory.CreateLogger<TfsIdentitySource>());

        var teamSource = new TfsTeamSource(
            collection,
            _loggerFactory.CreateLogger<TfsTeamSource>());
        var projectLifecycleService = new ProjectLifecycleService(
            _projectLifecycleNameGenerator,
            new TfsProjectLifecycleProvider(),
            _loggerFactory.CreateLogger<ProjectLifecycleService>());

        return new TfsJobServices(
            collection,
            workItemStore,
            revisionSource,
            attachmentSource,
            nodeCreator,
            classificationTreeReader,
            discoveryService,
            projectDiscoveryService,
            fetchService,
            tfsEndpoint,
            exportMetrics,
            attachmentMetrics,
            identitySource,
            teamSource,
            projectLifecycleService);
    }

    public void Dispose()
    {
        // Factory itself holds no per-job state — nothing to dispose.
    }
}

/// <summary>
/// Container for per-job TFS services. Disposes the TFS collection when the job ends.
/// </summary>
public sealed class TfsJobServices : IDisposable
{
    public WorkItemStore WorkItemStore { get; }
    public IWorkItemRevisionSource RevisionSource { get; }
    public IAttachmentBinarySource AttachmentSource { get; }
    public INodeCreator NodeCreator { get; }
    public IClassificationTreeReader ClassificationTreeReader { get; }
    public IWorkItemDiscoveryService DiscoveryService { get; }
    public IProjectDiscoveryService ProjectDiscoveryService { get; }
    public IWorkItemFetchService FetchService { get; }
    public TeamFoundationServerEndpointOptions Endpoint { get; }
    public IIdentitySource IdentitySource { get; }
    public ITeamSource TeamSource { get; }
    public IProjectLifecycleService ProjectLifecycleService { get; }

    public IWorkItemExportMetrics ExportMetrics { get; }
    public IAttachmentDownloadMetrics AttachmentMetrics { get; }

    private readonly TfsTeamProjectCollection _collection;

    public TfsJobServices(
        TfsTeamProjectCollection collection,
        WorkItemStore workItemStore,
        IWorkItemRevisionSource revisionSource,
        IAttachmentBinarySource attachmentSource,
        INodeCreator nodeCreator,
        IClassificationTreeReader classificationTreeReader,
        IWorkItemDiscoveryService discoveryService,
        IProjectDiscoveryService projectDiscoveryService,
        IWorkItemFetchService fetchService,
        TeamFoundationServerEndpointOptions endpoint,
        IWorkItemExportMetrics exportMetrics,
        IAttachmentDownloadMetrics attachmentMetrics,
        IIdentitySource identitySource,
        ITeamSource teamSource,
        IProjectLifecycleService projectLifecycleService)
    {
        _collection = collection;
        WorkItemStore = workItemStore;
        RevisionSource = revisionSource;
        AttachmentSource = attachmentSource;
        NodeCreator = nodeCreator;
        ClassificationTreeReader = classificationTreeReader;
        DiscoveryService = discoveryService;
        ProjectDiscoveryService = projectDiscoveryService;
        FetchService = fetchService;
        Endpoint = endpoint;
        ExportMetrics = exportMetrics;
        AttachmentMetrics = attachmentMetrics;
        IdentitySource = identitySource;
        TeamSource = teamSource;
        ProjectLifecycleService = projectLifecycleService;
    }

    public void Dispose()
    {
        _collection.Dispose();
    }
}

/// <summary>
/// Simple implementation of ISourceEndpointInfo for TFS jobs.
/// </summary>
internal sealed class SourceEndpointInfo : ISourceEndpointInfo
{
    public string Url { get; init; } = string.Empty;
    public string Project { get; init; } = string.Empty;
    public string ConnectorType { get; init; } = string.Empty;
    public string OrganisationSlug => EndpointSlugHelper.ExtractSlug(Url);

    // TFS uses its own SDK for auth — return a minimal endpoint for compatibility.
    public OrganisationEndpoint ToOrganisationEndpoint() =>
        new OrganisationEndpoint { ResolvedUrl = Url, Type = ConnectorType };
}
