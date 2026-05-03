// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Tools;

/// <summary>Severity level of a validation finding.</summary>
public enum FieldTransformValidationSeverity { Error, Warning, Info }

/// <summary>A single validation finding for a transform rule.</summary>
public sealed record FieldTransformValidationEntry(
    string GroupName,
    string TransformName,
    string Field,
    FieldTransformValidationSeverity Severity,
    string Message);

/// <summary>Aggregated validation report for all configured transform rules.</summary>
public sealed record FieldTransformValidationReport(bool IsValid, IReadOnlyList<FieldTransformValidationEntry> Entries);
