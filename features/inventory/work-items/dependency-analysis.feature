Feature: Work Item Dependency Analysis
    As a capability provider
    I want the dependency analysis service to correctly classify and record external work item links
    So that the migration engineer receives accurate dependency information

    Scenario: Cross-project link is recorded with all nine CSV fields
        Given a work item with a link to a work item in a different project (same organisation)
        When the dependency analysis runs
        Then a DependencyRecord is emitted with:
            | Field              | Value                      |
            | SourceWorkItemId   | (source work item ID)      |
            | SourceWorkItemType | (e.g., "User Story")       |
            | SourceProject      | (name of source project)   |
            | LinkType           | (e.g., "Parent", "Related")|
            | LinkScope          | CrossProject               |
            | TargetWorkItemId   | (target work item ID)      |
            | TargetProject      | (name of target project)   |
            | TargetOrganisation | (empty string)             |
            | TargetStatus       | Reachable or Deleted etc.  |

    Scenario: Same-project links are silently discarded and never reported
        Given a work item with a link to another work item in the same project
        When the dependency analysis runs
        Then no DependencyRecord is emitted for that link
        And the work item count includes the source item
        And the external links count is zero (or unchanged if there are other external links)

    Scenario: Cross-organisation links are recorded with LinkScope=CrossOrganisation
        Given a work item with a link to a work item in a different organisation
        When the dependency analysis runs
        Then a DependencyRecord is emitted with:
            | Field              | Value                              |
            | LinkScope          | CrossOrganisation                  |
            | TargetOrganisation | (remote organisation hostname)     |
            | TargetProject      | (empty or remote project name)     |
        And the record is distinct from cross-project records

    Scenario: Inaccessible targets are recorded with appropriate TargetStatus
        Given linked work items that are deleted, access-denied, or unreachable
        When the dependency analysis runs
        Then DependencyRecords are emitted with TargetStatus values:
            | Condition                           | TargetStatus   |
            | Target work item exists and readable| Reachable      |
            | Target work item is deleted         | Deleted        |
            | User lacks read permission on target| AccessDenied   |
            | Target URL unreachable              | Unknown        |
        And the analysis does not fail or throw exceptions
