using System;

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Registration record that links an IOptions&lt;T&gt; options type to its canonical
/// config section path. Register one entry per options type via
/// <see cref="SchemaOptionsEntryExtensions.AddSchemaEntry{T}"/>.
/// </summary>
public sealed record SchemaOptionsEntry
{
    /// <summary>The options type (e.g. typeof(WorkItemsModuleOptions)).</summary>
    public required Type OptionsType { get; init; }

    /// <summary>
    /// The dot-separated section path matching T.SectionName
    /// (e.g. "MigrationPlatform:Modules:WorkItems").
    /// </summary>
    public required string SectionPath { get; init; }

    /// <summary>Optional description injected into the schema's description field.</summary>
    public string? Description { get; init; }
}
