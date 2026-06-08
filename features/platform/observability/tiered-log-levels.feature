Feature: Tiered log level architecture
  As the platform
  I want the agent and control plane to have independent log level filters
  So that the package retains full diagnostic detail while the control plane buffers only what operators typically need

  Scenario: Agent writes at its configured level regardless of control plane level
    Given the agent diagnostic log level is set to "Debug"
    And the control plane deployment-level minimum is "Warning"
    When the agent emits log records at Debug, Information, Warning, and Error levels
    Then ".migration/Logs/agent.jsonl" in the package contains records at Debug and above

  Scenario: Standalone mode aligns control plane minimum with operator level
    Given an operator runs export with "--level Information" in standalone mode
    When the local control plane starts
    Then the control plane deployment-level minimum is set to "Information"
    And all Information and above records are available for live streaming
