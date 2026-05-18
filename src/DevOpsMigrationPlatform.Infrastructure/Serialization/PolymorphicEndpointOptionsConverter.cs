// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Serialization;

/// <summary>
/// <see cref="JsonConverter{T}"/> for <see cref="MigrationEndpointOptions"/>.
/// Reads the <c>type</c> discriminator field first, looks up the concrete type in
/// <see cref="EndpointOptionsTypeRegistry"/>, then deserialises the full JSON object.
/// </summary>
public sealed class PolymorphicEndpointOptionsConverter : JsonConverter<MigrationEndpointOptions>
{
    private readonly EndpointOptionsTypeRegistry _registry;

    public PolymorphicEndpointOptionsConverter(EndpointOptionsTypeRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public override MigrationEndpointOptions? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        if (!root.TryGetProperty("type", out var typeElement) &&
            !root.TryGetProperty("Type", out typeElement))
            throw new JsonException("Missing 'type' discriminator field in endpoint options JSON.");

        var discriminator = typeElement.GetString()
            ?? throw new JsonException("'type' discriminator field is null in endpoint options JSON.");

        if (!_registry.TryGetType(discriminator, out var concreteType) || concreteType is null)
            throw new JsonException(
                $"Unknown endpoint options type discriminator '{discriminator}'. " +
                "Register the type with AddEndpointOptionsType() before deserialising.");

        return (MigrationEndpointOptions?)root.Deserialize(concreteType, options);
    }

    public override void Write(
        Utf8JsonWriter writer,
        MigrationEndpointOptions value,
        JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}
