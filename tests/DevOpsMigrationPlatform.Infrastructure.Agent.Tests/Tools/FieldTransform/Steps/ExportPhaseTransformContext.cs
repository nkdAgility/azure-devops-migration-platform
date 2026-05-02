// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform.Steps;

/// <summary>
/// Shared state for the export-phase field-transform Reqnroll scenarios.
/// </summary>
public class ExportPhaseTransformContext
{
    public FieldTransformOptions Options { get; set; } = new FieldTransformOptions { Enabled = true };
    public bool? IsEnabledForExport { get; set; }
    public bool? IsEnabledForImport { get; set; }
}
