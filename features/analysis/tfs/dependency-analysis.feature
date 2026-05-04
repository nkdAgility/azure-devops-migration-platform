# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) Naked Agility Limited

@analysis @tfs
Feature: Dependency analysis as analyser

  Scenario: Dependencies_AnalyserRuns_ProducesDependenciesCsv
    Given a Team Foundation Server dependencies job
    When dependency analysis runs
    Then analysis/dependencies.csv is produced with at least one data row

  Scenario: Dependencies_NoLinksFound_EmitsWarning
    Given a Team Foundation Server dependencies job with no cross-project links
    When dependency analysis runs
    Then a zero-output warning is logged

  Scenario: Dependencies_RegisteredWithInventoryJob_RunsAfterInventoryPhase
    Given a Team Foundation Server inventory job with analysers enabled
    When the inventory pipeline executes
    Then dependency analysis tasks run in analyse phase ordering
