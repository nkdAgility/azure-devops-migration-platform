// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Export;

/// <summary>
/// Simulated implementation of <see cref="IWorkItemCommentSource"/>.
/// Streams N synthetic comments when <c>HasComments</c> is enabled; zero comments otherwise.
/// No network calls are made.
/// </summary>
public sealed class SimulatedWorkItemCommentSource : IWorkItemCommentSource
{
    private readonly bool _hasComments;
    private readonly int _commentsPerItem;

    public SimulatedWorkItemCommentSource(bool hasComments, int commentsPerItem = 2)
    {
        _hasComments = hasComments;
        _commentsPerItem = commentsPerItem;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<WorkItemComment> GetCommentsAsync(
        int workItemId,
        bool includeDeleted,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!_hasComments)
            yield break;

        for (int i = 1; i <= _commentsPerItem; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return new WorkItemComment
            {
                CommentId = $"simulated-{workItemId}-{i}",
                Version = 1,
                Text = $"[Simulated] Comment {i} on work item {workItemId}.",
                RenderedText = $"<p>[Simulated] Comment {i} on work item {workItemId}.</p>",
                Format = "html",
                IsDeleted = false,
                CreatedBy = new WorkItemIdentityRef
                {
                    DisplayName = "Simulated User",
                    UniqueName = "simulated@example.com"
                },
                CreatedDate = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(workItemId).AddHours(i)
            };
        }

        await System.Threading.Tasks.Task.CompletedTask;
    }
}
