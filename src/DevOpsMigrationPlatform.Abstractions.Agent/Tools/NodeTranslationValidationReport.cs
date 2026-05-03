// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Tools;

/// <summary>
/// Validation result from <see cref="INodeTranslationValidator.ValidateAsync"/>.
/// </summary>
/// <param name="IsValid"><c>true</c> if no validation findings.</param>
/// <param name="UnmappedPaths">Paths that no mapping rule matched.</param>
/// <param name="UnanchoredPaths">Paths not anchored in the source project (external paths).</param>
/// <param name="MalformedTargetPaths">
/// Replacement values that produce empty or ADO-illegal-character paths.
/// </param>
public sealed record NodeTranslationValidationReport(
    bool IsValid,
    IReadOnlyList<UnmappedPathFinding> UnmappedPaths,
    IReadOnlyList<UnmappedPathFinding> UnanchoredPaths,
    IReadOnlyList<string> MalformedTargetPaths);
