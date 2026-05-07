# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) Naked Agility Limited

@icapture @dependency-capture @inventory
Feature: Pure capture handlers for dependency discovery

  Scenario: DependencyCapture_PerOrgProjectTask_ExecutesAndWritesCsv
    Given a Dependencies job plan with capture.dependencies tasks for one org and one project
    When the job plan executor runs all pending capture tasks
    Then one DependencyCapture.CaptureAsync call is made per org+project combination
    And discovery/<org>/<project>/dependencies.csv is written to the artefact store

  Scenario: DependencyCapture_RegisteredAsICaptureOnly_IncludedInHandlerRegistry
    Given DependencyCapture is registered as ICapture only (not IModule) in the DI container
    When BuildCaptureHandlers assembles the captureHandlersByName dictionary
    Then DependencyCapture is included in captureHandlersByName alongside IModule capture handlers

  Scenario: DependencyCapture_ProducesPerProjectCsv_AnalyserConsumesUnchanged
    Given capture.dependencies tasks have been executed via DependencyCapture
    When the analyse.dependencies task runs via DependencyAnalyser
    Then DependencyAnalyser.AnalyseAsync consumes the per-project CSV paths written by DependencyCapture
    And no changes to DependencyAnalyser are required

  Scenario: DependencyCapture_SimulatedConnector_CompletesWithoutExternalConnectivity
    Given a Simulated-sourced Dependencies job plan with source.type equal to Simulated
    When the job plan executor runs DependencyCapture.CaptureAsync
    Then SimulatedDependencyDiscoveryServiceFactory resolves the dependency service
    And the capture completes without any external network call
    And the per-project dependencies.csv artefact is written
