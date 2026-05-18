// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Serialization;

/// <summary>
/// <see cref="JsonConverter{T}"/> for <see cref="OrganisationEntry"/>.
/// Reads the <c>type</c> discriminator field first, looks up the concrete type in
/// <see cref="EndpointOptionsTypeRegistry"/>, then deserialises the full JSON object.
/// </summary>
public sealed class PolymorphicOrganisationEntryConverter : JsonConverter<OrganisationEntry>
{
    private readonly EndpointOptionsTypeRegistry _registry;

    public PolymorphicOrganisationEntryConverter(EndpointOptionsTypeRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public override OrganisationEntry? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        if (!root.TryGetProperty("type", out var typeElement) &&
            !root.TryGetProperty("Type", out typeElement))
            throw new JsonException("Missing 'type' discriminator field in organisation entry JSON.");

        var discriminator = typeElement.GetString()
            ?? throw new JsonException("'type' discriminator field is null in organisation entry JSON.");

        if (!_registry.TryGetOrganisationEntryType(discriminator, out var concreteType) || concreteType is null)
            throw new JsonException(
                $"Unknown organisation entry type discriminator '{discriminator}'. " +
                "Register the type with AddOrganisationEntryType() before deserialising.");

        return (OrganisationEntry?)root.Deserialize(concreteType, options);
    }

    public override void Write(
        Utf8JsonWriter writer,
        OrganisationEntry value,
        JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}
