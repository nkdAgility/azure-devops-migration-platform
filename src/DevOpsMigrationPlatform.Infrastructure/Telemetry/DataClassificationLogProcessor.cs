#if !NETFRAMEWORK
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;

namespace DevOpsMigrationPlatform.Infrastructure.Telemetry;

/// <summary>
/// Registers a provider-level filter on <see cref="OpenTelemetryLoggerProvider"/>
/// that prevents <see cref="DataClassification.Customer"/> records from reaching
/// Azure Monitor / OTLP exporters. System, Derived, and unclassified logs pass
/// through. Uses the ambient <see cref="DataClassificationScope.Current"/>
/// value set by <see cref="DataClassificationExtensions.BeginDataScope"/>.
/// </summary>
public static class DataClassificationLogging
{
    /// <summary>
    /// Adds a logging filter that drops <see cref="DataClassification.Customer"/>
    /// records from the OpenTelemetry provider only. Other providers (e.g.
    /// PackageLoggerProvider) are unaffected.
    /// </summary>
    public static ILoggingBuilder AddDataClassificationFilter(this ILoggingBuilder builder)
    {
        // Filter at the provider level — evaluated on every ILogger.Log() call
        // before the OTel pipeline sees the record. AsyncLocal is current because
        // the filter runs synchronously on the caller's thread.
        builder.AddFilter<OpenTelemetryLoggerProvider>((category, level) =>
            DataClassificationScope.Current != DataClassification.Customer);
        return builder;
    }
}
#endif
