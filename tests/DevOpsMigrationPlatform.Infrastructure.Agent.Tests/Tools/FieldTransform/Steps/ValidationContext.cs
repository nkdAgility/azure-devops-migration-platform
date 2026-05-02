// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.FieldTransform.Steps;

/// <summary>
/// Shared state for the field-transform validation Reqnroll scenarios.
/// </summary>
public class ValidationContext
{
    public FieldTransformValidationReport? Report { get; set; }

    public Mock<IFieldDefinitionProvider> SourceProvider { get; } = new(MockBehavior.Strict);
    public Mock<IFieldDefinitionProviderFactory> ProviderFactory { get; } = new(MockBehavior.Strict);

    public List<FieldDefinition> SourceFields { get; } = new();
    public List<FieldTransformGroupOptions> Groups { get; } = new();

    public FieldTransformValidator BuildSut(bool useFactory = false)
    {
        var options = new FieldTransformOptions
        {
            Enabled = true,
            TransformGroups = Groups
        };

        if (useFactory)
        {
            SourceProvider
                .Setup(p => p.GetFieldDefinitionsAsync(It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(SourceFields);
            ProviderFactory.Setup(f => f.Create("source")).Returns(SourceProvider.Object);

            return new FieldTransformValidator(
                Options.Create(options),
                NullLogger<FieldTransformValidator>.Instance,
                ProviderFactory.Object);
        }

        return new FieldTransformValidator(
            Options.Create(options),
            NullLogger<FieldTransformValidator>.Instance);
    }
}
