# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) Naked Agility Limited

@analysis @ado
Feature: Dependency analysis as analyser

  Scenario: Dependencies_AnalyserRuns_ProducesDependenciesCsv
    Given an Azure DevOps dependencies job
    When dependency analysis runs
    Then analysis/dependencies.csv is produced with at least one data row

  Scenario: Dependencies_NoLinksFound_EmitsWarning
    Given an Azure DevOps dependencies job with no cross-project links
    When dependency analysis runs
    Then a zero-output warning is logged

  Scenario: Dependencies_RegisteredWithInventoryJob_RunsAfterInventoryPhase
    Given an Azure DevOps inventory job with analysers enabled
    When the inventory pipeline executes
    Then dependency analysis tasks run in analyse phase ordering
