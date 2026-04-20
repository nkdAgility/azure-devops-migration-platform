@feature:exclusive-package-lock @phase:platform
Feature: Exclusive Package Lock
  As an operator running concurrent migration agents
  I want the second agent to hard-bounce immediately when a live lock exists
  So that package data integrity is guaranteed

  Background:
    Given a migration package exists at a temporary directory

  @offline
  Scenario: Second agent is hard-bounced when live lock exists
    Given an agent with instance ID "agent-001" holds the lock on the package
    And the ControlPlane reports agent "agent-001" as Active
    When a second agent attempts to acquire the lock on the package
    Then the second agent receives a PackageLockConflictException
    And the exception reports owner agent instance "agent-001"

  @offline
  Scenario: Stale lock is replaced and agent proceeds normally
    Given a stale lock file exists in the package Checkpoints directory for agent "agent-stale"
    And the ControlPlane reports agent "agent-stale" as not found
    When an agent attempts to acquire the lock on the package
    Then the stale lock is deleted
    And the new agent acquires the lock successfully
    And no PackageLockConflictException is thrown

  @offline
  Scenario: Lock is released when job completes
    Given an agent with instance ID "agent-001" holds the lock on the package
    When the job completes and the lock handle is disposed
    Then the lock file no longer exists in the package Checkpoints directory
