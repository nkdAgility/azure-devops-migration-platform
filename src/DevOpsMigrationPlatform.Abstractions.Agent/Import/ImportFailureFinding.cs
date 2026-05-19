// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.Import;

/// <summary>
/// Structured finding emitted by a prepare-time import failure pattern.
/// </summary>
/// <param name="PatternCode">Stable machine identifier for the failure pattern.</param>
/// <param name="Severity">Whether the finding is blocking or warning.</param>
/// <param name="EvidenceKey">Stable locator used for rerun diffing and diagnostics.</param>
/// <param name="Message">Operator-readable reason.</param>
/// <param name="SuggestedAction">Operator-readable remediation guidance.</param>
public sealed record ImportFailureFinding(
    string PatternCode,
    ImportFailureSeverity Severity,
    string EvidenceKey,
    string Message,
    string SuggestedAction);

