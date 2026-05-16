// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if !NET481
using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Import.Configuration;

internal sealed class WorkItemImportOptionsValidator : IValidateOptions<WorkItemImportOptions>
{
    public ValidateOptionsResult Validate(string? name, WorkItemImportOptions options)
    {
        var errors = new List<string>();

        if (!options.RevisionReplay)
        {
            if (options.LinkReplay)
                errors.Add("Extensions:WorkItemImport:LinkReplay requires RevisionReplay to be enabled.");

            if (options.AttachmentReplay)
                errors.Add("Extensions:WorkItemImport:AttachmentReplay requires RevisionReplay to be enabled.");

            if (options.EmbeddedImageReplay)
                errors.Add("Extensions:WorkItemImport:EmbeddedImageReplay requires RevisionReplay to be enabled.");

            if (options.FieldTransform)
                errors.Add("Extensions:WorkItemImport:FieldTransform requires RevisionReplay to be enabled.");
        }

        return errors.Count > 0 ? ValidateOptionsResult.Fail(errors) : ValidateOptionsResult.Success;
    }
}
#endif
