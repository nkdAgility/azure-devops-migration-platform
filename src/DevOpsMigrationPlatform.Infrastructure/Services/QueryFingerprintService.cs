using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DevOpsMigrationPlatform.Infrastructure.Services;

/// <summary>
/// SHA-256 deterministic fingerprint from normalised WIQL query text and
/// lexicographically sorted parameters. Post-fetch filters are excluded.
/// </summary>
public sealed class QueryFingerprintService : IQueryFingerprintService
{
    /// <summary>
    /// WIQL keywords that are uppercased during normalisation so that
    /// semantically identical queries with different casing produce the same fingerprint.
    /// </summary>
    private static readonly string[] WiqlKeywords =
    {
        "SELECT", "FROM", "WHERE", "ORDER BY", "ASC", "DESC",
        "AND", "OR", "NOT", "IN", "UNDER", "EVER", "CONTAINS", "LIKE"
    };

    /// <inheritdoc />
    public string Compute(string queryText, IReadOnlyDictionary<string, string>? parameters = null)
    {
        if (string.IsNullOrWhiteSpace(queryText))
            throw new ArgumentException("Query text must not be null or whitespace.", nameof(queryText));

        var normalised = NormaliseQuery(queryText);
        var input = new StringBuilder(normalised);

        if (parameters is { Count: > 0 })
        {
            // Append sorted parameters so the same query with the same parameters
            // always produces the same fingerprint regardless of dictionary order.
            foreach (var kvp in parameters.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                input.Append('\n');
                input.Append(kvp.Key);
                input.Append('=');
                input.Append(kvp.Value);
            }
        }

        return ComputeSha256(input.ToString());
    }

    /// <summary>
    /// Normalises the query text: collapse whitespace, trim, uppercase WIQL keywords.
    /// </summary>
    internal static string NormaliseQuery(string query)
    {
        // 1. Collapse all contiguous whitespace to a single space.
        var collapsed = Regex.Replace(query, @"\s+", " ");

        // 2. Trim leading and trailing whitespace.
        collapsed = collapsed.Trim();

        // 3. Uppercase WIQL keywords (case-insensitive replacement).
        foreach (var keyword in WiqlKeywords)
        {
            collapsed = Regex.Replace(
                collapsed,
                @"\b" + Regex.Escape(keyword) + @"\b",
                keyword,
                RegexOptions.IgnoreCase);
        }

        return collapsed;
    }

    private static string ComputeSha256(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
#if NET481
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(bytes);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
#else
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
#endif
    }
}
