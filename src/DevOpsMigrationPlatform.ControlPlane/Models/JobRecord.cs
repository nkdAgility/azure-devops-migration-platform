using System;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.ControlPlane.Models;

/// <summary>
/// Control-plane runtime wrapper around a <see cref="Job"/>.
/// Pairs the immutable job definition with mutable runtime state tracked by <see cref="Services.JobStore"/>.
/// </summary>
public sealed record JobRecord(
    Job Job,
    string State,
    string SubmittedByUpn,
    DateTimeOffset SubmittedAt
);
