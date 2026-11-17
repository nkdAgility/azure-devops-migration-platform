// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Globalization;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.WorkItems.Revisions;

/// <summary>
/// Writes TFS Object Model field values into the package in a culture-invariant form (#169).
/// Dates become ISO 8601 UTC round-trip strings, the form the REST connector writes and Azure
/// DevOps accepts; numbers use the invariant culture. Other values keep their string form.
/// </summary>
public static class TfsFieldValueFormatter
{
    public static string? Format(object? value) => value switch
    {
        null => null,
        DateTime date => (date.Kind == DateTimeKind.Utc ? date : date.ToUniversalTime())
            .ToString("o", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };
}
