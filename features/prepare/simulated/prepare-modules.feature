# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) Naked Agility Limited

@prepare @simulated
Feature: Prepare phase writes per-module reports

  Scenario: Prepare_AllModulesEnabled_WritesReportPerModule
    Given a simulated prepare job with all prepare-capable modules enabled
    When the prepare job is executed
    Then WorkItems/prepare-report.json exists
    And Identities/prepare-report.json exists
    And Nodes/prepare-report.json exists
    And Teams/prepare-report.json exists

  Scenario: Prepare_UnresolvedIdentities_CompletesWithWarning
    Given a simulated prepare job with unresolved identities
    When the prepare job is executed
    Then the job completes with warning telemetry

  Scenario: Prepare_InMigratePipeline_RunsBeforeImport
    Given a migrate job in simulated mode
    When the pipeline executes
    Then prepare runs before import

