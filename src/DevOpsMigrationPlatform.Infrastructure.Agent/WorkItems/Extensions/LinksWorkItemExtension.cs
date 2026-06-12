// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Extensions;

/// <summary>
/// Work-item capability: adds related links, external links, and hyperlinks to the target work item
/// during import. The link-application business rule, expressed as an <see cref="IModuleExtension"/>
/// port, independent of the checkpoint/resume delivery mechanism that drives it.
/// </summary>
public sealed class LinksWorkItemExtension : IModuleExtension
{
    private readonly IWorkItemTarget _target;
    private readonly LinksExtensionOptions _options;

    public LinksWorkItemExtension(IWorkItemTarget target, IOptions<LinksExtensionOptions> options)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
    }

    public string Module => "WorkItems";
    public string Name => "Links";
    public int Order => 300;
    public bool SupportsExport => false;
    public bool SupportsImport => true;
    public bool IsEnabled => _options.Enabled;

    public Task ExportAsync(IExtensionContext context, CancellationToken ct) => Task.CompletedTask;

    public Task ImportAsync(IExtensionContext context, CancellationToken ct)
    {
        if (context is not WorkItemExtensionContext ctx)
            throw new ArgumentException($"Expected {nameof(WorkItemExtensionContext)}.", nameof(context));

        var revision = ctx.Revision;
        return _target.AddLinksAsync(
            ctx.TargetWorkItemId,
            revision.RelatedLinks,
            revision.ExternalLinks,
            revision.Hyperlinks,
            ct);
    }
}
