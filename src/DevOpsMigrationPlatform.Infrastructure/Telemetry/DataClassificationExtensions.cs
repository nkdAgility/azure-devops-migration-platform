// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Telemetry;

/// <summary>
/// Extension methods for creating data classification logging scopes on <see cref="ILogger"/>.
/// Sets both the ambient <see cref="DataClassificationScope.Current"/> (for OTel processor
/// and custom logger providers) and pushes a structured scope state for OTel scope walkers.
/// </summary>
public static class DataClassificationExtensions
{
    /// <summary>
    /// Begins a logging scope that classifies all enclosed log statements with
    /// the specified <paramref name="classification"/>. The innermost scope wins
    /// when scopes are nested.
    /// </summary>
    public static IDisposable BeginDataScope(this ILogger logger, DataClassification classification)
    {
        // Set the ambient AsyncLocal for the OTel processor and custom loggers.
        var ambientScope = DataClassificationScope.Begin(classification);

        // Push a structured scope so OTel scope-walking exporters can also see the value.
        var state = new DataClassificationState(classification);
        var loggerScope = logger.BeginScope(state);

        return new CombinedScopeDisposable(ambientScope, loggerScope);
    }

    private sealed class CombinedScopeDisposable : IDisposable
    {
        private readonly IDisposable _ambientScope;
        private readonly IDisposable? _loggerScope;

        public CombinedScopeDisposable(IDisposable ambientScope, IDisposable? loggerScope)
        {
            _ambientScope = ambientScope;
            _loggerScope = loggerScope;
        }

        public void Dispose()
        {
            _loggerScope?.Dispose();
            _ambientScope.Dispose();
        }
    }
}
