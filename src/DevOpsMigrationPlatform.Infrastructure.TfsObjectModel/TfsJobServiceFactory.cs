using System;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Attachments;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Discovery;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Export;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Options;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.Client;
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

    public TfsJobServiceFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
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

        // Authenticate — PAT or Windows-integrated depending on config.
        VssClientCredentials creds;
        if (tfsEndpoint.Authentication?.Type == AuthenticationType.Pat &&
            !string.IsNullOrEmpty(tfsEndpoint.Authentication.ResolvedAccessToken))
        {
            creds = new VssClientCredentials(
                new Microsoft.VisualStudio.Services.Common.WindowsCredential(false),
                new Microsoft.VisualStudio.Services.Common.VssBasicCredential(
                    string.Empty, tfsEndpoint.Authentication.ResolvedAccessToken),
                Microsoft.VisualStudio.Services.Common.CredentialPromptType.DoNotPrompt);
        }
        else
        {
            creds = new VssClientCredentials(true); // Windows-integrated
        }

        var collection = new TfsTeamProjectCollection(serverUrl, creds);
        collection.EnsureAuthenticated();

        var workItemStore = new WorkItemStore(collection, WorkItemStoreFlags.BypassRules);

        // Shared attachment registry — links revision enumeration to binary download.
        var attachmentRegistry = new TfsAttachmentRegistry();

#pragma warning disable CS0618 // Obsolete metrics — retained for TFS path until migration to IMigrationMetrics
        var exportMetrics = new WorkItemExportMetrics();
        var attachmentMetrics = new AttachmentDownloadMetrics();
#pragma warning restore CS0618

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

        var wiqlQuery = $"SELECT * FROM WorkItems WHERE [System.TeamProject] = '{project}'";

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

        var classificationTreeReader = new TfsClassificationTreeReader(
            collection,
            _loggerFactory.CreateLogger<TfsClassificationTreeReader>());

        var discoveryService = new TfsObjectModelWorkItemDiscoveryService(
            workItemStore,
            queryStrategy);

        var projectDiscoveryService = new TfsProjectDiscoveryService(workItemStore);

        var fetchService = new TfsWorkItemFetchService(
            workItemStore,
            queryStrategy);

        return new TfsJobServices(
            collection,
            revisionSource,
            attachmentSource,
            classificationTreeReader,
            discoveryService,
            projectDiscoveryService,
            fetchService,
            tfsEndpoint,
            exportMetrics,
            attachmentMetrics);
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
    public IWorkItemRevisionSource RevisionSource { get; }
    public IAttachmentBinarySource AttachmentSource { get; }
    public IClassificationTreeReader ClassificationTreeReader { get; }
    public IWorkItemDiscoveryService DiscoveryService { get; }
    public IProjectDiscoveryService ProjectDiscoveryService { get; }
    public IWorkItemFetchService FetchService { get; }
    public TeamFoundationServerEndpointOptions Endpoint { get; }

#pragma warning disable CS0618
    public IWorkItemExportMetrics ExportMetrics { get; }
    public IAttachmentDownloadMetrics AttachmentMetrics { get; }
#pragma warning restore CS0618

    private readonly TfsTeamProjectCollection _collection;

    public TfsJobServices(
        TfsTeamProjectCollection collection,
        IWorkItemRevisionSource revisionSource,
        IAttachmentBinarySource attachmentSource,
        IClassificationTreeReader classificationTreeReader,
        IWorkItemDiscoveryService discoveryService,
        IProjectDiscoveryService projectDiscoveryService,
        IWorkItemFetchService fetchService,
        TeamFoundationServerEndpointOptions endpoint,
#pragma warning disable CS0618
        IWorkItemExportMetrics exportMetrics,
        IAttachmentDownloadMetrics attachmentMetrics)
#pragma warning restore CS0618
    {
        _collection = collection;
        RevisionSource = revisionSource;
        AttachmentSource = attachmentSource;
        ClassificationTreeReader = classificationTreeReader;
        DiscoveryService = discoveryService;
        ProjectDiscoveryService = projectDiscoveryService;
        FetchService = fetchService;
        Endpoint = endpoint;
        ExportMetrics = exportMetrics;
        AttachmentMetrics = attachmentMetrics;
    }

    public void Dispose()
    {
        _collection.Dispose();
    }
}
