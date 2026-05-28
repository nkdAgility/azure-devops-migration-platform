// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Configuration;

internal sealed class WorkItemOptionsValidator : IValidateOptions<WorkItemOptions>
{
    public ValidateOptionsResult Validate(string? name, WorkItemOptions options)
    {
        var errors = new List<string>();

        if (!options.RevisionReplay)
        {
            if (options.LinkReplay)
                errors.Add("Extensions:WorkItem:LinkReplay requires RevisionReplay to be enabled.");

            if (options.AttachmentReplay)
                errors.Add("Extensions:WorkItem:AttachmentReplay requires RevisionReplay to be enabled.");

            if (options.EmbeddedImageReplay)
                errors.Add("Extensions:WorkItem:EmbeddedImageReplay requires RevisionReplay to be enabled.");

            if (options.FieldTransform)
                errors.Add("Extensions:WorkItem:FieldTransform requires RevisionReplay to be enabled.");
        }

        return errors.Count > 0 ? ValidateOptionsResult.Fail(errors) : ValidateOptionsResult.Success;
    }
}
