using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Simulated.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Export;

/// <summary>
/// Generates deterministic work item revisions from a <see cref="SimulatedEndpointOptions"/> generator config.
/// No network calls are made. Revisions are streamed lazily via <c>yield return</c>.
/// </summary>
public sealed class SimulatedWorkItemRevisionSource : IWorkItemRevisionSource
{
    private readonly SimulatedGeneratorConfig _config;

    // Fixed epoch so all generated timestamps are deterministic and reproducible.
    private static readonly DateTimeOffset _epoch = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public SimulatedWorkItemRevisionSource(SimulatedGeneratorConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));

        // Validate at construction so failures surface at job startup, not mid-stream.
        foreach (var project in _config.Projects)
        {
            if (string.IsNullOrWhiteSpace(project.Name))
                throw new InvalidOperationException(
                    "Config error: A SimulatedProjectConfig entry has an empty 'name'.");

            foreach (var wit in project.WorkItemTypes)
            {
                if (string.IsNullOrWhiteSpace(wit.Type))
                    throw new InvalidOperationException(
                        $"Config error: A SimulatedWorkItemTypeConfig in project '{project.Name}' has an empty 'type'.");

                if (wit.RevisionsPerItem < 1)
                    throw new InvalidOperationException(
                        $"Config error: SimulatedWorkItemTypeConfig '{wit.Type}' in project '{project.Name}' has " +
                        $"RevisionsPerItem={wit.RevisionsPerItem}. Must be ≥ 1.");
            }
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<WorkItemRevision> GetRevisionsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        int workItemId = 1;

        foreach (var project in _config.Projects)
        {
            foreach (var typeConfig in project.WorkItemTypes)
            {
                for (int itemIndex = 0; itemIndex < typeConfig.Count; itemIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    int thisWorkItemId = workItemId++;

                    for (int revIndex = 0; revIndex < typeConfig.RevisionsPerItem; revIndex++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // Deterministic timestamp: epoch + workItemId days + revIndex hours
                        var changedDate = _epoch
                            .AddDays(thisWorkItemId)
                            .AddHours(revIndex);

                        var fields = BuildFields(
                            thisWorkItemId, revIndex, typeConfig.Type, project.Name, changedDate);

                        var attachments = revIndex == 0 && project.AttachmentSizeKb > 0
                            ? BuildAttachments(thisWorkItemId, project.AttachmentSizeKb)
                            : Array.Empty<AttachmentMetadata>();

                        yield return new WorkItemRevision
                        {
                            WorkItemId = thisWorkItemId,
                            RevisionIndex = revIndex,
                            ChangedDate = changedDate,
                            Fields = fields,
                            Attachments = attachments
                        };
                    }
                }
            }
        }

        // Satisfy the compiler's requirement that the method is truly async.
        await System.Threading.Tasks.Task.CompletedTask;
    }

    private static WorkItemField[] BuildFields(
        int workItemId,
        int revisionIndex,
        string workItemType,
        string projectName,
        DateTimeOffset changedDate)
    {
        var state = revisionIndex == 0 ? "New" : "Active";
        return new[]
        {
            new WorkItemField { ReferenceName = "System.Id",           Value = workItemId.ToString() },
            new WorkItemField { ReferenceName = "System.Rev",          Value = (revisionIndex + 1).ToString() },
            new WorkItemField { ReferenceName = "System.WorkItemType", Value = workItemType },
            new WorkItemField { ReferenceName = "System.TeamProject",  Value = projectName },
            new WorkItemField { ReferenceName = "System.State",        Value = state },
            new WorkItemField { ReferenceName = "System.Title",        Value = $"[Simulated] {workItemType} {workItemId}" },
            new WorkItemField { ReferenceName = "System.CreatedDate",  Value = changedDate.ToString("O") },
            new WorkItemField { ReferenceName = "System.ChangedDate",  Value = changedDate.ToString("O") },
            new WorkItemField { ReferenceName = "System.CreatedBy",    Value = "simulated@example.com" },
            new WorkItemField { ReferenceName = "System.ChangedBy",    Value = "simulated@example.com" },
        };
    }

    private static AttachmentMetadata[] BuildAttachments(int workItemId, int attachmentSizeKb)
    {
        var fileName = $"attachment-{workItemId}.bin";
        return new[]
        {
            new AttachmentMetadata
            {
                OriginalName = fileName,
                RelativePath = fileName,
                Sha256 = ComputeDeterministicHash(workItemId, attachmentSizeKb),
                Size = attachmentSizeKb * 1024L
            }
        };
    }

    private static string ComputeDeterministicHash(int workItemId, int attachmentSizeKb)
    {
        // Generate a deterministic pseudo-hash string from the inputs.
        var seed = workItemId * 31 + attachmentSizeKb;
        return seed.GetHashCode().ToString("x8").PadLeft(64, '0');
    }
}
