using System.Collections;
using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Telemetry;

/// <summary>
/// An immutable, ordered list of OTel dimension tags used across all metrics interfaces.
/// Implements <see cref="IReadOnlyList{T}"/> of <see cref="KeyValuePair{TKey,TValue}"/>
/// so it is usable on all targets including net481 without any <c>#if</c> guards,
/// and can be passed to any API that accepts the interface.
/// </summary>
public sealed class MetricsTagList : IReadOnlyList<KeyValuePair<string, object?>>
{
    private readonly List<KeyValuePair<string, object?>> _tags;

    /// <summary>Creates an empty tag list (enables collection-initializer syntax).</summary>
    public MetricsTagList() => _tags = new List<KeyValuePair<string, object?>>();

    private MetricsTagList(List<KeyValuePair<string, object?>> tags) => _tags = tags;

    /// <summary>Adds a tag (enables collection-initializer syntax).</summary>
    public void Add(string key, object? value) => _tags.Add(new KeyValuePair<string, object?>(key, value));

    /// <summary>Adds a tag from a KeyValuePair (enables <c>new("key","value")</c> collection-initializer syntax).</summary>
    public void Add(KeyValuePair<string, object?> tag) => _tags.Add(tag);

    /// <inheritdoc/>
    public KeyValuePair<string, object?> this[int index] => _tags[index];

    /// <inheritdoc/>
    public int Count => _tags.Count;

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _tags.GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => _tags.GetEnumerator();

    /// <summary>An empty tag list.</summary>
    public static MetricsTagList Empty { get; } = new(new List<KeyValuePair<string, object?>>());

    /// <summary>
    /// Creates a tag list with the three mandatory dimension tags.
    /// </summary>
    public static MetricsTagList Create(string jobId, string operation, string module) =>
        new(new List<KeyValuePair<string, object?>>
        {
            new("job.id", jobId),
            new("operation", operation),
            new("module", module),
        });

    /// <summary>
    /// Creates a tag list with mandatory tags plus the optional <c>source.type</c> tag.
    /// </summary>
    public static MetricsTagList Create(string jobId, string operation, string module, string sourceType) =>
        new(new List<KeyValuePair<string, object?>>
        {
            new("job.id", jobId),
            new("operation", operation),
            new("module", module),
            new("source.type", sourceType),
        });
}
