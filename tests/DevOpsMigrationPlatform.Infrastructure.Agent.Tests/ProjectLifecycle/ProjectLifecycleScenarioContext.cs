// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.ProjectLifecycle;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.ProjectLifecycle;

public sealed class ProjectLifecycleScenarioContext
{
    public ProjectLifecycleContext? Context { get; set; }
    public ProjectLifecycleRecord? Record { get; set; }
    public bool SimulatedExecutionFailure { get; set; }
    public bool TeardownAttempted { get; set; }
    public LifecycleEligibilityFlag Eligibility { get; set; } = LifecycleEligibilityFlag.Disabled;
}
