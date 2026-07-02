// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent;
using Cap = DevOpsMigrationPlatform.Abstractions.Agent.ConnectorCapability;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Teams.Extensions;

/// <summary>
/// Teams module extension: exports and imports team member assignments as a separate
/// <c>Teams/{slug}/members.json</c> artifact. Translates member identities via
/// <see cref="IIdentityTranslationTool"/> (when available) during import.
/// </summary>
public sealed class TeamMembersTeamExtension : IModuleExtension
{
    private static readonly JsonSerializerOptions s_writeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private static readonly JsonSerializerOptions s_readOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly TeamMembersExtensionOptions _options;
    private readonly IConnectorCapabilityProvider _capProvider;
    private readonly ITeamSource _teamSource;
    private readonly ITeamTarget _teamTarget;
    private readonly IIdentityTranslationTool? _identityTranslationTool;
    private readonly ILogger<TeamMembersTeamExtension>? _logger;

    public TeamMembersTeamExtension(
        IOptions<TeamMembersExtensionOptions> options,
        IConnectorCapabilityProvider capProvider,
        ITeamSource teamSource,
        ITeamTarget teamTarget,
        IIdentityTranslationTool? identityTranslationTool = null,
        ILogger<TeamMembersTeamExtension>? logger = null)
    {
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        _capProvider = capProvider ?? throw new ArgumentNullException(nameof(capProvider));
        _teamSource = teamSource ?? throw new ArgumentNullException(nameof(teamSource));
        _teamTarget = teamTarget ?? throw new ArgumentNullException(nameof(teamTarget));
        _identityTranslationTool = identityTranslationTool;
        _logger = logger;
    }

    public string Module => "Teams";
    public string Name => "TeamMembers";
    public int Order => 30;
    public bool SupportsExport => _capProvider.Has(Cap.TeamMembers);
    public bool SupportsImport => _capProvider.Has(Cap.TeamMembers);
    public bool IsEnabled => _options.Enabled;

    public async Task ExportAsync(IExtensionContext context, CancellationToken ct)
    {
        if (context is not TeamExtensionContext ctx)
            throw new ArgumentException($"Expected {nameof(TeamExtensionContext)}.", nameof(context));


        var members = new List<TeamMember>();
        try
        {
            await foreach (var member in _teamSource.GetTeamMembersAsync(ctx.ProjectName, ctx.EntityId, ct).ConfigureAwait(false))
                members.Add(member);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[TeamMembers] Failed to fetch members for team '{TeamName}' — skipping.", ctx.Team.Name);
            return;
        }

        var json = JsonSerializer.Serialize(members, s_writeOptions);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json), writable: false);
        await ctx.Package.PersistContentAsync(
            new PackageContentContext(
                PackageContentKind.Artefact,
                Organisation: ctx.Organisation,
                Project: ctx.ProjectName,
                Module: "Teams",
                Address: new TeamArtifactAddress(ctx.Slug, "members.json")),
            new PackagePayload(stream, "application/json"),
            ct).ConfigureAwait(false);

        _logger?.LogInformation("[TeamMembers] Exported {Count} members for team '{TeamName}' → Teams/{Slug}/members.json.",
            members.Count, ctx.Team.Name, ctx.Slug);
    }

    public async Task ImportAsync(IExtensionContext context, CancellationToken ct)
    {
        if (context is not TeamExtensionContext ctx)
            throw new ArgumentException($"Expected {nameof(TeamExtensionContext)}.", nameof(context));


        if (string.IsNullOrEmpty(ctx.TargetEntityId))
        {
            _logger?.LogWarning("[TeamMembers] TargetEntityId not set for team '{TeamName}' — skipping members import.", ctx.Team.Name);
            return;
        }

        var payload = await ctx.Package.RequestContentAsync(
            new PackageContentContext(
                PackageContentKind.Artefact,
                Organisation: ctx.Organisation,
                Project: ctx.ProjectName,
                Module: "Teams",
                Address: new TeamArtifactAddress(ctx.Slug, "members.json")),
            ct).ConfigureAwait(false);

        if (payload is null)
        {
            _logger?.LogDebug("[TeamMembers] No members.json found for team '{TeamName}' — skipping.", ctx.Team.Name);
            return;
        }

        string json;
        using var reader = new StreamReader(payload.Content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: false);
        json = await reader.ReadToEndAsync().ConfigureAwait(false);

        List<TeamMember>? members;
        try
        {
            members = JsonSerializer.Deserialize<List<TeamMember>>(json, s_readOptions);
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex, "[TeamMembers] Malformed members.json for team '{TeamName}' — skipping.", ctx.Team.Name);
            return;
        }

        if (members is null || members.Count == 0)
        {
            _logger?.LogDebug("[TeamMembers] No members in members.json for team '{TeamName}' — skipping.", ctx.Team.Name);
            return;
        }

        var identityEnabled = _identityTranslationTool?.IsEnabled == true;

        foreach (var member in members)
        {
            try
            {
                var resolvedDescriptor = identityEnabled
                    ? _identityTranslationTool!.Translate(member.Descriptor)
                    : member.Descriptor;

                // GAP-006/FR-010: when identity translation falls back to the configured default
                // (i.e. the source member could not be resolved on the target), skip the add and
                // log a structured warning rather than importing the member under the wrong identity.
                var defaultIdentity = _identityTranslationTool?.DefaultIdentity;
                if (identityEnabled
                    && !string.IsNullOrEmpty(defaultIdentity)
                    && string.Equals(resolvedDescriptor, defaultIdentity, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(member.Descriptor, defaultIdentity, StringComparison.OrdinalIgnoreCase))
                {
                    _logger?.LogWarning(
                        "[TeamMembers] Member '{MemberDescriptor}' ({Member}) resolved to the configured default identity — skipping add to team '{Team}' (unresolvable member).",
                        member.Descriptor, member.DisplayName, ctx.Team.Name);
                    continue;
                }

                var resolvedMember = member with { Descriptor = resolvedDescriptor };
                await _teamTarget.AddMemberAsync(ctx.ProjectName, ctx.TargetEntityId!, resolvedMember, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex,
                    "[TeamMembers] Failed to add member '{Member}' to team '{Team}' — skipping.",
                    member.DisplayName, ctx.Team.Name);
            }
        }

        _logger?.LogInformation("[TeamMembers] Imported members for team '{TeamName}'.", ctx.Team.Name);
    }
}
