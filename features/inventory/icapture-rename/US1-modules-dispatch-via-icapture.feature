# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) Naked Agility Limited

@icapture @modules @inventory
Feature: Modules dispatch via ICapture

  Scenario: CaptureAsync_WorkItemsModule_CalledPerTask
    Given a simulated capture job plan with capture.workitems tasks for one org and one project
    When the job plan executor runs all pending tasks
    Then WorkItemsModule.CaptureAsync is called for each capture.workitems task
    And the expected inventory artefact is written to the artefact store

  Scenario: SupportsInventory_WithIModuleInheritsICapture_PreservesCapturePlanning
    Given an IModule implementation where SupportsInventory returns true
    When the plan builder enumerates inventory-capable modules
    Then the module produces capture.* tasks in the execution plan

  Scenario: CaptureDispatch_SingleHandlerDictionary_NoModuleTypeBranching
    Given a capture handler registered in captureHandlersByName by name
    When a capture task referencing that handler name is executed
    Then the executor resolves the handler from captureHandlersByName without branching on module type
