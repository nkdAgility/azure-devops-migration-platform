Feature: TUI Log Panel - Diagnostics Toggle
  As an operator
  I want to toggle the Log Panel between progress events and diagnostic logs
  So that I can switch between operational progress and internal agent diagnostics without leaving the TUI

  Scenario: Toggling Log Panel to Diagnostics mode streams diagnostic records
    Given a job is selected and the Log Panel is in Progress mode
    When the operator presses Tab within the Log Panel
    Then the panel switches to Diagnostics mode
    And diagnostic log records stream in real time from GET /jobs/{jobId}/diagnostics?follow=true
    And records are displayed with visual level indicators: Information white, Warning yellow, Error red
    And the panel header shows "Log [Diagnostics]"

  Scenario: Level filter in Diagnostics mode excludes lower-level records
    Given the Log Panel is in Diagnostics mode
    When the operator applies a level filter of Warning and above
    Then only records at or above Warning level are displayed

  Scenario: Control plane minimum level filters records before they reach the TUI
    Given the control plane Diagnostics MinimumLevel is set to Information
    When the agent emits a Debug record
    Then that record does not appear in the Log Panel Diagnostics mode
