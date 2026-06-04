@us1-config-write
Feature: Config applied on export (package config)
  As a migration operator
  I want tool configuration to travel inside the package
  So that the Migration Agent can read tool configuration without the original config file

  # GAP-007 (RESOLVED 2026-06-04): the previous @us1-write-idempotency scenario asserted that
  # the CLI fails fast when migration-config.json already exists in the package. That is
  # architecturally impossible: the CLI has NO access to the package filesystem (Separation of
  # Planes — the CLI talks only to the control plane; only the agent writes the package). The
  # pre-submission check it described cannot exist, so the scenario was deleted (no production
  # code change). The existing-file case is handled by the AGENT via resume semantics:
  # it overwrites migration-config.json when the endpoints are unchanged, and rejects with
  # InvalidOperationException when the endpoints changed. See docs/configuration-reference.md.

  Background:
    Given a valid migration.json configuration with field-transform and node-translation tools enabled

  Scenario: Agent applies resume semantics when migration-config.json already exists
    Given a package already containing migration-config.json with unchanged endpoints
    When the Migration Agent processes the job
    Then the agent overwrites migration-config.json and continues

  Scenario: Agent rejects a config whose endpoints changed
    Given a package already containing migration-config.json with different endpoints
    When the Migration Agent processes the job
    Then the agent rejects the job with InvalidOperationException
