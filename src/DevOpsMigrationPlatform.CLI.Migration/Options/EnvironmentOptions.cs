// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace DevOpsMigrationPlatform.CLI.Migration.Options;

/// <summary>
/// Describes the execution environment for the migration platform.
/// Bound from the <c>MigrationPlatform:Environment</c> configuration section.
/// When missing, defaults to <see cref="EnvironmentType.Standalone"/> with
/// <c>ControlPlane.BaseUrl = "http://localhost:5100"</c>.
/// </summary>
public sealed class EnvironmentOptions : IValidatableObject
{
    public const string SectionName = "MigrationPlatform:Environment";

    /// <summary>
    /// Whether the platform runs as a standalone local deployment or a hosted cloud deployment.
    /// Default: <see cref="EnvironmentType.Standalone"/>.
    /// </summary>
    public EnvironmentType Type { get; init; } = EnvironmentType.Standalone;

    /// <summary>Control plane connection settings.</summary>
    public ControlPlaneEndpointOptions ControlPlane { get; init; } = new();

    /// <summary>
    /// Optional agent runner configuration for hosted environments that need
    /// cross-tenant or cross-subscription agent execution.
    /// Must be <c>null</c> when <see cref="Type"/> is <see cref="EnvironmentType.Standalone"/>.
    /// </summary>
    public AgentRunnerOptions? AgentRunner { get; init; }

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Type == EnvironmentType.Standalone && AgentRunner is not null)
        {
            yield return new ValidationResult(
                "AgentRunner must be null when Environment.Type is Standalone.",
                [nameof(AgentRunner)]);
        }

        if (Type == EnvironmentType.Hosted &&
            string.IsNullOrWhiteSpace(ControlPlane.BaseUrl))
        {
            yield return new ValidationResult(
                "ControlPlane.BaseUrl must be provided when Environment.Type is Hosted.",
                [nameof(ControlPlane)]);
        }

        if (AgentRunner is not null)
        {
            foreach (var result in AgentRunner.Validate(new ValidationContext(AgentRunner)))
                yield return result;
        }
    }
}

/// <summary>Execution environment type.</summary>
public enum EnvironmentType
{
    /// <summary>Local or dedicated-server deployment. The CLI uses <c>LocalStackHost</c> to start <c>ControlPlaneHost</c> and <c>MigrationAgent</c> as child processes.</summary>
    Standalone,

    /// <summary>Cloud or remote deployment. The CLI connects to a remote control plane.</summary>
    Hosted
}

/// <summary>Control plane HTTP endpoint configuration.</summary>
public sealed class ControlPlaneEndpointOptions
{
    /// <summary>Base URL of the running control plane, e.g. <c>http://localhost:5100</c>.</summary>
    public string BaseUrl { get; init; } = "http://localhost:5100";
}

/// <summary>
/// Configuration for the agent runner in hosted environments.
/// Passed through to the control plane layer; no deployment logic runs in the CLI.
/// </summary>
public sealed class AgentRunnerOptions : IValidatableObject
{
    /// <summary>Runner type, e.g. <c>AzureContainerApps</c>.</summary>
    [Required]
    public string Type { get; init; } = string.Empty;

    /// <summary>Azure subscription ID. Supports <c>$ENV:VAR</c> resolution.</summary>
    [Required]
    public string SubscriptionId { get; init; } = string.Empty;

    /// <summary>Azure resource group name. Supports <c>$ENV:VAR</c> resolution.</summary>
    [Required]
    public string ResourceGroup { get; init; } = string.Empty;

    /// <summary>Azure Container Apps environment name. Supports <c>$ENV:VAR</c> resolution.</summary>
    [Required]
    public string EnvironmentName { get; init; } = string.Empty;

    /// <summary>Authentication credentials for the agent runner.</summary>
    public AgentRunnerAuthOptions? Auth { get; init; }

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Type))
            yield return new ValidationResult("AgentRunner.Type is required.", [nameof(Type)]);

        if (string.IsNullOrWhiteSpace(SubscriptionId))
            yield return new ValidationResult("AgentRunner.SubscriptionId is required.", [nameof(SubscriptionId)]);

        if (string.IsNullOrWhiteSpace(ResourceGroup))
            yield return new ValidationResult("AgentRunner.ResourceGroup is required.", [nameof(ResourceGroup)]);

        if (string.IsNullOrWhiteSpace(EnvironmentName))
            yield return new ValidationResult("AgentRunner.EnvironmentName is required.", [nameof(EnvironmentName)]);

        if (Auth is null)
        {
            yield return new ValidationResult(
                "AgentRunner.Auth is required when AgentRunner is provided.", [nameof(Auth)]);
        }
        else
        {
            foreach (var result in Auth.Validate(new ValidationContext(Auth)))
                yield return result;
        }
    }
}

/// <summary>Authentication credentials for the agent runner.</summary>
public sealed class AgentRunnerAuthOptions : IValidatableObject
{
    /// <summary>Authentication type, e.g. <c>ServicePrincipal</c>.</summary>
    [Required]
    public string Type { get; init; } = string.Empty;

    /// <summary>Azure AD tenant ID. Supports <c>$ENV:VAR</c> resolution.</summary>
    [Required]
    public string TenantId { get; init; } = string.Empty;

    /// <summary>Service principal client ID. Supports <c>$ENV:VAR</c> resolution.</summary>
    [Required]
    public string ClientId { get; init; } = string.Empty;

    /// <summary>Service principal client secret. Supports <c>$ENV:VAR</c> resolution. Never logged.</summary>
    [Required]
    public string ClientSecret { get; init; } = string.Empty;

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Type))
            yield return new ValidationResult("Auth.Type is required.", [nameof(Type)]);

        if (string.IsNullOrWhiteSpace(TenantId))
            yield return new ValidationResult("Auth.TenantId is required.", [nameof(TenantId)]);

        if (string.IsNullOrWhiteSpace(ClientId))
            yield return new ValidationResult("Auth.ClientId is required.", [nameof(ClientId)]);

        if (string.IsNullOrWhiteSpace(ClientSecret))
            yield return new ValidationResult("Auth.ClientSecret is required.", [nameof(ClientSecret)]);
    }
}
