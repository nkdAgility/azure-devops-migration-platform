// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Teams;

/// <summary>
/// Serialises <see cref="TeamAreaPaths"/> in the Azure DevOps teamfieldvalues REST shape:
/// <c>{ "field": { "referenceName": … }, "defaultValue": …, "values": [ { "value": …, "includeChildren": … } ] }</c>.
/// Reading additionally accepts the legacy package shape
/// (<c>{ "defaultAreaPath": …, "includedAreaPaths": [ "…" ] }</c>) and bare-string
/// <c>values</c> entries; both default <c>includeChildren</c> to <c>true</c>, matching the
/// platform's historical write behaviour, so pre-existing packages import unchanged.
/// </summary>
public sealed class TeamAreaPathsJsonConverter : JsonConverter<TeamAreaPaths>
{
    /// <inheritdoc/>
    public override TeamAreaPaths? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException($"Expected an object for {nameof(TeamAreaPaths)}, got {root.ValueKind}.");

        var defaultValue = GetString(root, "defaultValue") ?? GetString(root, "defaultAreaPath") ?? string.Empty;

        var fieldReferenceName = TeamAreaPaths.AreaPathFieldReferenceName;
        if (TryGetProperty(root, "field", out var field) && field.ValueKind == JsonValueKind.Object)
        {
            var reference = GetString(field, "referenceName");
            if (!string.IsNullOrEmpty(reference))
                fieldReferenceName = reference!;
        }

        var values = new List<TeamFieldValueEntry>();
        if (TryGetProperty(root, "values", out var apiValues) && apiValues.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in apiValues.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.String)
                {
                    var value = entry.GetString();
                    if (!string.IsNullOrEmpty(value))
                        values.Add(new TeamFieldValueEntry(value!, IncludeChildren: true));
                }
                else if (entry.ValueKind == JsonValueKind.Object)
                {
                    var value = GetString(entry, "value");
                    if (string.IsNullOrEmpty(value))
                        continue;
                    // Absent or null includeChildren defaults to true — the historical behaviour.
                    var includeChildren = !TryGetProperty(entry, "includeChildren", out var flag)
                        || flag.ValueKind != JsonValueKind.False;
                    values.Add(new TeamFieldValueEntry(value!, includeChildren));
                }
            }
        }
        else if (TryGetProperty(root, "includedAreaPaths", out var legacyValues) && legacyValues.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in legacyValues.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.String)
                    continue;
                var value = entry.GetString();
                if (!string.IsNullOrEmpty(value))
                    values.Add(new TeamFieldValueEntry(value!, IncludeChildren: true));
            }
        }

        return new TeamAreaPaths(defaultValue, values, fieldReferenceName);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, TeamAreaPaths value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteStartObject("field");
        writer.WriteString("referenceName", value.FieldReferenceName);
        writer.WriteEndObject();
        writer.WriteString("defaultValue", value.DefaultValue);
        writer.WriteStartArray("values");
        foreach (var entry in value.Values)
        {
            writer.WriteStartObject();
            writer.WriteString("value", entry.Value);
            writer.WriteBoolean("includeChildren", entry.IncludeChildren);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? GetString(JsonElement element, string name)
        => TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
