using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace DevOpsMigrationPlatform.Abstractions.Telemetry;

/// <summary>
/// Ambient data classification scope backed by <see cref="AsyncLocal{T}"/>.
/// The innermost scope wins when scopes are nested.
/// </summary>
public static class DataClassificationScope
{
    /// <summary>
    /// The scope key used to store the current data classification in logging scopes.
    /// </summary>
    public const string ScopeKey = "DataClassification";

    private static readonly AsyncLocal<DataClassification?> _current = new();

    /// <summary>
    /// Gets the current data classification from the ambient scope.
    /// Returns <c>null</c> when no classification scope is active (defaults to System).
    /// </summary>
    public static DataClassification? Current => _current.Value;

    /// <summary>
    /// Sets the ambient data classification and returns a disposable that restores the
    /// previous value on disposal.
    /// </summary>
    public static IDisposable Begin(DataClassification classification)
    {
        var previous = _current.Value;
        _current.Value = classification;
        return new ScopeDisposable(previous);
    }

    private sealed class ScopeDisposable : IDisposable
    {
        private readonly DataClassification? _previous;

        public ScopeDisposable(DataClassification? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            _current.Value = _previous;
        }
    }
}

/// <summary>
/// Zero-allocation struct implementing <see cref="IReadOnlyList{T}"/> for use as
/// a logging scope state. Contains a single key-value pair with the data classification.
/// </summary>
public readonly struct DataClassificationState : IReadOnlyList<KeyValuePair<string, object>>
{
    private readonly KeyValuePair<string, object> _entry;

    public DataClassificationState(DataClassification classification)
    {
        _entry = new KeyValuePair<string, object>(
            DataClassificationScope.ScopeKey,
            classification.ToString());
    }

    public int Count => 1;

    public KeyValuePair<string, object> this[int index]
        => index == 0
            ? _entry
            : throw new ArgumentOutOfRangeException(nameof(index));

    public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
    {
        yield return _entry;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => $"{_entry.Key}={_entry.Value}";
}
