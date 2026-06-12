// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Extensions;

/// <summary>
/// Work-item capability: replays inline work-item comments onto the target work item during import. The
/// comment-replay business rule, expressed as an <see cref="IModuleExtension"/> port, independent of the
/// checkpoint/resume delivery mechanism that drives it.
/// </summary>
public sealed class CommentsWorkItemExtension : IModuleExtension
{
    private readonly CommentsExtensionOptions _options;

    public CommentsWorkItemExtension(IOptions<CommentsExtensionOptions> options)
        => _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;

    public string Module => "WorkItems";
    public string Name => "Comments";
    public int Order => 500;
    public bool SupportsExport => false;
    public bool SupportsImport => true;
    public bool IsEnabled => _options.Enabled;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public Task ExportAsync(IExtensionContext context, CancellationToken ct) => Task.CompletedTask;

    public async Task ImportAsync(IExtensionContext context, CancellationToken ct)
    {
        if (context is not WorkItemExtensionContext ctx)
            throw new ArgumentException($"Expected {nameof(WorkItemExtensionContext)}.", nameof(context));
        if (ctx.Target is null)
            throw new ArgumentException("WorkItemExtensionContext.Target is required for import.", nameof(context));
        if (ctx.ReadTextAsync is null)
            throw new ArgumentException("WorkItemExtensionContext.ReadTextAsync is required for comment import.", nameof(context));

        var commentJson = await ctx.ReadTextAsync($"{ctx.FolderPath}/comment.json", ct).ConfigureAwait(false);
        if (commentJson is null) return;

        var comments = JsonSerializer.Deserialize<List<WorkItemComment>>(commentJson, _jsonOptions);
        if (comments is null || comments.Count == 0) return;

        foreach (var comment in comments)
        {
            if (comment.IsDeleted) continue;
            var text = comment.RenderedText ?? comment.Text;
            await ctx.Target.CreateCommentAsync(ctx.TargetWorkItemId, text, ct).ConfigureAwait(false);
        }
    }
}
