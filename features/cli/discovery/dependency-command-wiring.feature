Feature: Discovery Dependencies CLI Command Wiring
    As a migration engineer
    I want to run the discovery dependencies command to identify cross-project and cross-organisation links
    So that I can plan my migration scope and understand dependency risks

    Scenario: Command runs and writes CSV to current working directory
        Given a discovery config file pointing to a test organisation and project
        When I run the command `discovery dependencies --config migration.json`
        Then the command exits with code 0
        And a file `discovery-dependencies.csv` is written to the current working directory
        And the CSV file contains a header row with columns: SourceWorkItemId, SourceWorkItemType, SourceProject, LinkType, LinkScope, TargetWorkItemId, TargetProject, TargetOrganisation, TargetStatus
        And the terminal output contains a success message mentioning the number of external links found

    Scenario: No external dependencies found reports empty CSV with header
        Given a discovery config pointing to a project with no cross-project or cross-organisation links
        When I run the command `discovery dependencies --config migration.json`
        Then the command exits with code 0
        And the output file `discovery-dependencies.csv` contains only the header row (no data rows)
        And the terminal output displays "No external dependencies found."

    Scenario: Cross-organisation links are flagged with warning in console output
        Given a discovery config pointing to a project with at least one cross-organisation link
        When I run the command `discovery dependencies --config migration.json`
        Then the command exits with code 0
        And the CSV file is written with all external links (both cross-project and cross-org)
        And the terminal summary table shows a separate count for cross-organisation links
        And the cross-organisation count is marked with a visual warning symbol (⚠)
        And the terminal output includes the text "ACTION REQUIRED"

    Scenario: Custom output path is respected
        Given a discovery config file
        When I run the command `discovery dependencies --config migration.json --output ./reports/deps.csv`
        Then the command exits with code 0
        And the output file is written to `./reports/deps.csv` instead of the default location
        And the terminal output references the custom path in the summary
