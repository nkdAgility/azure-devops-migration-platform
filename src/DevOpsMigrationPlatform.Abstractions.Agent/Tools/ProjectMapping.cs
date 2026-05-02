// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.Tools;

/// <summary>
/// Holds source and target project name context passed to <see cref="INodeTranslationTool.TranslatePath"/>.
/// </summary>
/// <param name="SourceProjectName">The source project name (from config/manifest).</param>
/// <param name="TargetProjectName">The target project name (from config).</param>
public sealed record ProjectMapping(string SourceProjectName, string TargetProjectName);
