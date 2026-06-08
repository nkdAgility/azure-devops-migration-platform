# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) Naked Agility Limited

@icapture @dependency-capture @inventory
Feature: Pure capture handlers for dependency discovery

  Scenario: DependencyCapture_ProducesPerProjectCsv_AnalyserConsumesUnchanged
    Given capture.dependencies tasks have been executed via DependencyCapture
    When the analyse.dependencies task runs via DependencyAnalyser
    Then DependencyAnalyser.AnalyseAsync consumes the per-project CSV paths written by DependencyCapture
    And no changes to DependencyAnalyser are required
