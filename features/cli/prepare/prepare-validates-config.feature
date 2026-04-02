Feature: CLI loads and applies migration configuration
  As a platform operator
  I want the CLI to load my migration.json overlaid on built-in defaults
  So that I only need to specify values that differ from the defaults

  Scenario: Default artefacts path is applied when migration.json omits it
    Given no migration.json file exists
    When the platform options are loaded from defaults only
    Then the artefacts path is "%userprofile%\.DevOpsMigrationPlatform"

  Scenario: Default policies are applied when migration.json omits them
    Given no migration.json file exists
    When the platform options are loaded from defaults only
    Then the max retries is 8
    And the max concurrency is 4

  Scenario: User config overrides the default artefacts path
    Given a migration.json with artefacts path "C:\my-exports"
    When the platform options are loaded
    Then the artefacts path is "C:\my-exports"

  Scenario: User config overrides the default retry policy
    Given a migration.json with max retries 3
    When the platform options are loaded
    Then the max retries is 3

  Scenario: Artefacts path expands environment variables
    Given a migration.json with artefacts path "%TEMP%\migration-run"
    When the platform options are loaded
    Then the expanded artefacts path does not contain "%TEMP%"
    And the expanded artefacts path contains the value of the TEMP environment variable
