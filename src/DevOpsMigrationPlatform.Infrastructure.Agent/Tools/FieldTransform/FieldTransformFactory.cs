using System;
using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform.Transforms;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform;

/// <summary>
/// Creates <see cref="IFieldTransform"/> instances from rule options.
/// Implementations for each type discriminator are registered via <see cref="Register"/>.
/// Built-in transforms are registered in the constructor; additional phases add more.
/// </summary>
public sealed class FieldTransformFactory : IFieldTransformFactory
{
    private static readonly HashSet<string> IdentityFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "System.CreatedBy",
        "System.ChangedBy",
        "System.AuthorizedAs"
    };

    private readonly Dictionary<string, Func<FieldTransformRuleOptions, string, int, IFieldTransform>> _registry
        = new Dictionary<string, Func<FieldTransformRuleOptions, string, int, IFieldTransform>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initialises the factory and registers all built-in transform types.
    /// </summary>
    /// <param name="loggerFactory">
    /// Logger factory used when creating transform instances.
    /// Falls back to <see cref="NullLoggerFactory.Instance"/> when <c>null</c>.
    /// </param>
    public FieldTransformFactory(ILoggerFactory? loggerFactory = null)
    {
        var lf = loggerFactory ?? NullLoggerFactory.Instance;
        RegisterBuiltInTransforms(lf);
    }

    private void RegisterBuiltInTransforms(ILoggerFactory lf)
    {
        Register("CopyField", (options, groupName, ordinal) =>
        {
            if (string.IsNullOrWhiteSpace(options.SourceField))
                throw new InvalidOperationException("CopyField transform requires 'SourceField'.");
            if (string.IsNullOrWhiteSpace(options.TargetField))
                throw new InvalidOperationException("CopyField transform requires 'TargetField'.");

            var name = string.IsNullOrWhiteSpace(options.Name)
                ? $"{groupName}.CopyField{ordinal}"
                : options.Name!;

            return new CopyFieldTransform(
                name,
                groupName,
                options.SourceField!,
                options.TargetField!,
                options.DefaultValue);
        });

        Register("CopyFieldBatch", (options, groupName, ordinal) =>
        {
            if (options.FieldMappings == null || options.FieldMappings.Count == 0)
                throw new InvalidOperationException("CopyFieldBatch transform requires a non-empty 'FieldMappings'.");

            var name = string.IsNullOrWhiteSpace(options.Name)
                ? $"{groupName}.CopyFieldBatch{ordinal}"
                : options.Name!;

            return new CopyFieldBatchTransform(
                name,
                groupName,
                options.FieldMappings);
        });

        Register("MapValue", (options, groupName, ordinal) =>
        {
            if (string.IsNullOrWhiteSpace(options.Field))
                throw new InvalidOperationException("MapValue transform requires 'Field'.");
            if (options.ValueMap == null || options.ValueMap.Count == 0)
                throw new InvalidOperationException("MapValue transform requires a non-empty 'ValueMap'.");

            var name = string.IsNullOrWhiteSpace(options.Name)
                ? $"{groupName}.MapValue{ordinal}"
                : options.Name!;

            return new MapValueTransform(
                name,
                groupName,
                options.Field!,
                options.ValueMap,
                options.ApplyTo,
                lf.CreateLogger<MapValueTransform>());
        });

        Register("ExcludeField", (options, groupName, ordinal) =>
        {
            if (string.IsNullOrWhiteSpace(options.Field))
                throw new InvalidOperationException("ExcludeField transform requires 'Field'.");

            var name = string.IsNullOrWhiteSpace(options.Name)
                ? $"{groupName}.ExcludeField{ordinal}"
                : options.Name!;

            return new ExcludeFieldTransform(name, groupName, options.Field!);
        });

        Register("ClearField", (options, groupName, ordinal) =>
        {
            if (string.IsNullOrWhiteSpace(options.Field))
                throw new InvalidOperationException("ClearField transform requires 'Field'.");

            var name = string.IsNullOrWhiteSpace(options.Name)
                ? $"{groupName}.ClearField{ordinal}"
                : options.Name!;

            return new ClearFieldTransform(name, groupName, options.Field!);
        });

        Register("SetField", (options, groupName, ordinal) =>
        {
            if (string.IsNullOrWhiteSpace(options.Field))
                throw new InvalidOperationException("SetField transform requires 'Field'.");
            if (options.Value == null)
                throw new InvalidOperationException("SetField transform requires 'Value'.");

            var name = string.IsNullOrWhiteSpace(options.Name)
                ? $"{groupName}.SetField{ordinal}"
                : options.Name!;

            return new SetFieldTransform(name, groupName, options.Field!, options.Value);
        });

        Register("CalculateField", (options, groupName, ordinal) =>
        {
            if (string.IsNullOrWhiteSpace(options.Field))
                throw new InvalidOperationException("CalculateField transform requires 'Field'.");
            if (string.IsNullOrWhiteSpace(options.Expression))
                throw new InvalidOperationException("CalculateField transform requires 'Expression'.");

            var name = string.IsNullOrWhiteSpace(options.Name)
                ? $"{groupName}.CalculateField{ordinal}"
                : options.Name!;

            return new CalculateFieldTransform(
                name,
                groupName,
                options.Field!,
                options.Expression!,
                new SimpleExpressionEvaluator(),
                lf.CreateLogger<CalculateFieldTransform>());
        });

        Register("FieldToTag", (options, groupName, ordinal) =>
        {
            if (string.IsNullOrWhiteSpace(options.Field))
                throw new InvalidOperationException("FieldToTag transform requires 'Field'.");

            var name = string.IsNullOrWhiteSpace(options.Name)
                ? $"{groupName}.FieldToTag{ordinal}"
                : options.Name!;

            return new FieldToTagTransform(name, groupName, options.Field!);
        });

        Register("ConditionalTag", (options, groupName, ordinal) =>
        {
            if (string.IsNullOrWhiteSpace(options.Field))
                throw new InvalidOperationException("ConditionalTag transform requires 'Field'.");
            if (string.IsNullOrWhiteSpace(options.Condition))
                throw new InvalidOperationException("ConditionalTag transform requires 'Condition'.");
            if (string.IsNullOrWhiteSpace(options.Tag))
                throw new InvalidOperationException("ConditionalTag transform requires 'Tag'.");

            var name = string.IsNullOrWhiteSpace(options.Name)
                ? $"{groupName}.ConditionalTag{ordinal}"
                : options.Name!;

            return new ConditionalTagTransform(name, groupName, options.Field!, options.Condition!, options.Tag!);
        });

        Register("MergeToTag", (options, groupName, ordinal) =>
        {
            if (options.SourceFields == null || options.SourceFields.Count == 0)
                throw new InvalidOperationException("MergeToTag transform requires a non-empty 'SourceFields'.");

            var name = string.IsNullOrWhiteSpace(options.Name)
                ? $"{groupName}.MergeToTag{ordinal}"
                : options.Name!;

            return new MergeToTagTransform(name, groupName, options.SourceFields);
        });

        Register("TreeToTag", (options, groupName, ordinal) =>
        {
            if (string.IsNullOrWhiteSpace(options.Field))
                throw new InvalidOperationException("TreeToTag transform requires 'Field'.");

            var name = string.IsNullOrWhiteSpace(options.Name)
                ? $"{groupName}.TreeToTag{ordinal}"
                : options.Name!;

            return new TreeToTagTransform(name, groupName, options.Field!);
        });

        Register("RegexField", (options, groupName, ordinal) =>
        {
            if (string.IsNullOrWhiteSpace(options.Field))
                throw new InvalidOperationException("RegexField transform requires 'Field'.");
            if (string.IsNullOrWhiteSpace(options.Pattern))
                throw new InvalidOperationException("RegexField transform requires 'Pattern'.");
            if (options.Replacement == null)
                throw new InvalidOperationException("RegexField transform requires 'Replacement'.");

            var name = string.IsNullOrWhiteSpace(options.Name)
                ? $"{groupName}.RegexField{ordinal}"
                : options.Name!;

            return new RegexFieldTransform(
                name,
                groupName,
                options.Field!,
                options.Pattern!,
                options.Replacement,
                lf.CreateLogger<RegexFieldTransform>());
        });

        Register("MergeFields", (options, groupName, ordinal) =>
        {
            if (options.SourceFields == null || options.SourceFields.Count == 0)
                throw new InvalidOperationException("MergeFields transform requires a non-empty 'SourceFields'.");
            if (string.IsNullOrWhiteSpace(options.Field))
                throw new InvalidOperationException("MergeFields transform requires 'Field' (target field).");
            if (options.FormatString == null)
                throw new InvalidOperationException("MergeFields transform requires 'FormatString'.");

            var name = string.IsNullOrWhiteSpace(options.Name)
                ? $"{groupName}.MergeFields{ordinal}"
                : options.Name!;

            return new MergeFieldsTransform(
                name,
                groupName,
                options.SourceFields,
                options.Field!,
                options.FormatString);
        });

        Register("ConditionalField", (options, groupName, ordinal) =>
        {
            if (string.IsNullOrWhiteSpace(options.Field))
                throw new InvalidOperationException("ConditionalField transform requires 'Field' (condition field).");
            if (string.IsNullOrWhiteSpace(options.Condition))
                throw new InvalidOperationException("ConditionalField transform requires 'Condition'.");
            if (options.TrueValue == null && options.FalseValue == null)
                throw new InvalidOperationException("ConditionalField transform requires at least one of 'TrueValue' or 'FalseValue'.");

            var name = string.IsNullOrWhiteSpace(options.Name)
                ? $"{groupName}.ConditionalField{ordinal}"
                : options.Name!;

            var targetField = string.IsNullOrWhiteSpace(options.TargetField)
                ? options.Field!
                : options.TargetField!;

            return new ConditionalFieldTransform(
                name,
                groupName,
                options.Field!,
                options.Condition!,
                targetField,
                options.TrueValue,
                options.FalseValue,
                lf.CreateLogger<ConditionalFieldTransform>());
        });
    }

    /// <summary>Registers a factory function for the given type discriminator.</summary>
    public void Register(string type, Func<FieldTransformRuleOptions, string, int, IFieldTransform> factory)
        => _registry[type] = factory;

    /// <inheritdoc />
    public IFieldTransform Create(FieldTransformRuleOptions options, string groupName, int ordinal)
    {
        if (string.IsNullOrWhiteSpace(options.Type))
            throw new InvalidOperationException("FieldTransformRuleOptions.Type must not be empty.");

        // Identity field guard (FR-021)
        if (options.Field != null && IdentityFields.Contains(options.Field))
            throw new InvalidOperationException($"Field '{options.Field}' is an identity field and cannot be transformed.");

        if (options.TargetField != null && IdentityFields.Contains(options.TargetField))
            throw new InvalidOperationException($"Field '{options.TargetField}' is an identity field and cannot be transformed.");

        if (!_registry.TryGetValue(options.Type, out var factory))
            throw new InvalidOperationException(
                $"Unknown FieldTransform type: '{options.Type}'. Supported types: CopyField, CopyFieldBatch, SetField, MapValue, MergeFields, CalculateField, ClearField, ExcludeField, FieldToTag, ConditionalTag, MergeToTag, ConditionalField, RegexField, TreeToTag.");

        return factory(options, groupName, ordinal);
    }
}
