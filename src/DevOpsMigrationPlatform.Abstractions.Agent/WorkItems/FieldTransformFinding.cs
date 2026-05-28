// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

public enum FieldTransformFindingStatus
{
    Valid = 0,
    FieldNotFound = 1,
    TypeMismatch = 2,
    Error = 3
}

public sealed record FieldTransformFinding(
    string FieldName,
    string TypeName,
    string TransformRule,
    FieldTransformFindingStatus Status,
    string Recommendation);

