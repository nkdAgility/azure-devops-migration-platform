# Extraction Summary: observability-tiered-log-levels

## Scenario 1: Agent writes at its configured level regardless of control plane level
- Given: agent diagnostic log level = Debug, control plane minimum = Warning (independent)
- When: agent emits Debug, Information, Warning, Error records
- Then: package contains all four levels

Extracted intent: PackageLoggerProvider.MinimumLevel is independent of ControlPlaneLoggerProvider.MinimumLevel.
The agent filter controls what goes into the package file; the control plane filter controls what is buffered in memory for streaming.

## Scenario 2: Standalone mode aligns control plane minimum with operator level
- Given: operator runs export --level Information in standalone mode
- When: local control plane starts
- Then: control plane deployment-level minimum = Information; Information+ records are available for streaming

Extracted intent: DiagnosticLogStore rejects records below its configured MinimumLevel.
When MinimumLevel="Information", Debug records are discarded and Information/Warning/Error are retained.
