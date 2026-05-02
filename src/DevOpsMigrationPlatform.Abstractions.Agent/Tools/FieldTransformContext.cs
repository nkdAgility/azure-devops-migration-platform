// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.Tools;

/// <summary>Contextual metadata passed to each field transform during execution.</summary>
public sealed record FieldTransformContext(int WorkItemId, int RevisionIndex, string WorkItemType, FieldTransformPhase Phase);
