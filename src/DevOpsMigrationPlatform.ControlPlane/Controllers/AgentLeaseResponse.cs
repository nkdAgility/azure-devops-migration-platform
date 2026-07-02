// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.ControlPlane.Models;

/// <summary>Response body returned when an agent successfully acquires a job lease.</summary>
public sealed record AgentLeaseResponse(string LeaseId, Job Job);
