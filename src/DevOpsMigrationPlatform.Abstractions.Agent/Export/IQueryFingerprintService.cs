using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Services;

/// <summary>
/// Computes a deterministic fingerprint from a WIQL query text and optional parameters.
/// Used to detect query changes between runs and reject unsafe continuation.
/// </summary>
public interface IQueryFingerprintService
{
    /// <summary>
    /// Computes a SHA-256 fingerprint from the normalised query text and
    /// lexicographically sorted parameters.
    /// </summary>
    /// <param name="queryText">The WIQL query text (will be normalised before hashing).</param>
    /// <param name="parameters">Optional query parameters included in the fingerprint.</param>
    /// <returns>A hex-encoded SHA-256 hash string.</returns>
    string Compute(string queryText, IReadOnlyDictionary<string, string>? parameters = null);
}
