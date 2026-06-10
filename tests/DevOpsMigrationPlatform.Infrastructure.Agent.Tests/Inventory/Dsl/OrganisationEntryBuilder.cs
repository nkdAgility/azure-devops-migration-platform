// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Text.Json;
using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Inventory.Dsl;

/// <summary>
/// Fluent builder for <see cref="SimulatedOrganisationEntry"/> with optional WIQL and filter scopes.
/// All factory methods return connector-agnostic <see cref="OrganisationEntry"/> instances backed
/// by the simulated connector — no real ADO credentials required.
/// </summary>
internal static class OrganisationEntryBuilder
{
    /// <summary>Returns a baseline enabled simulated entry with no scopes.</summary>
    public static SimulatedOrganisationEntry Default(string orgKey = "sim-org") =>
        BuildEntry(orgKey, scopes: new List<MigrationPlatformOptionsScope>());

    /// <summary>Returns an entry with a wiql scope carrying <paramref name="query"/>.</summary>
    public static SimulatedOrganisationEntry WithWiqlScope(string query, string orgKey = "sim-org") =>
        BuildEntry(orgKey, new List<MigrationPlatformOptionsScope>
        {
            BuildWiqlScope(query)
        });

    /// <summary>
    /// Returns an entry with a wiql scope carrying an empty query string.
    /// Used to verify empty-string fallback to platform default.
    /// </summary>
    public static SimulatedOrganisationEntry WithEmptyWiqlScope(string orgKey = "sim-org") =>
        BuildEntry(orgKey, new List<MigrationPlatformOptionsScope>
        {
            new()
            {
                Type = "wiql",
                Parameters = new Dictionary<string, JsonElement>
                {
                    ["query"] = JsonDocument.Parse("\"\"").RootElement.Clone()
                }
            }
        });

    /// <summary>
    /// Returns an entry with one filter scope entry.
    /// <paramref name="mode"/> is "include" or "exclude".
    /// </summary>
    public static SimulatedOrganisationEntry WithFilterScope(
        string field, string pattern, string mode = "include", string orgKey = "sim-org") =>
        BuildEntry(orgKey, new List<MigrationPlatformOptionsScope>
        {
            BuildFilterScope(field, pattern, mode)
        });

    /// <summary>Returns an entry with both a wiql scope and a filter scope.</summary>
    public static SimulatedOrganisationEntry WithWiqlAndFilterScope(
        string query, string field, string pattern, string mode = "include", string orgKey = "sim-org") =>
        BuildEntry(orgKey, new List<MigrationPlatformOptionsScope>
        {
            BuildWiqlScope(query),
            BuildFilterScope(field, pattern, mode)
        });

    // ── Private helpers ────────────────────────────────────────────────────────

    private static SimulatedOrganisationEntry BuildEntry(
        string orgKey,
        List<MigrationPlatformOptionsScope> scopes) =>
        new()
        {
            Type = "Simulated",
            Enabled = true,
            Scopes = scopes,
            Generator = new SimulatedGeneratorConfig
            {
                // One synthetic project named "TestProject" per org.
                // InventoryServiceHarness.WithOrganisation overrides project discovery via mock,
                // so the generator config projects list is only a fallback.
                Projects = new List<SimulatedProjectConfig>
                {
                    new() { Name = "TestProject" }
                }
            }
        };

    private static MigrationPlatformOptionsScope BuildWiqlScope(string query) =>
        new()
        {
            Type = "wiql",
            Parameters = new Dictionary<string, JsonElement>
            {
                ["query"] = JsonDocument.Parse($"\"{EscapeJson(query)}\"").RootElement.Clone()
            }
        };

    private static MigrationPlatformOptionsScope BuildFilterScope(
        string field, string pattern, string mode) =>
        new()
        {
            Type = "filter",
            Parameters = new Dictionary<string, JsonElement>
            {
                ["field"]   = JsonDocument.Parse($"\"{EscapeJson(field)}\"").RootElement.Clone(),
                ["pattern"] = JsonDocument.Parse($"\"{EscapeJson(pattern)}\"").RootElement.Clone(),
                ["mode"]    = JsonDocument.Parse($"\"{EscapeJson(mode)}\"").RootElement.Clone()
            }
        };

    private static string EscapeJson(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
