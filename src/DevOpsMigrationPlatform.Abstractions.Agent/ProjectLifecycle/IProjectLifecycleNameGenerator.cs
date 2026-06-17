// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.ProjectLifecycle;

public interface IProjectLifecycleNameGenerator
{
    string Generate(string runId, string connectorType, string? namePrefix = null);
}
