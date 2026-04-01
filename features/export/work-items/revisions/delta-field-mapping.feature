Feature: Delta Field Mapping Per Revision
  As a migration operator
  I want each revision to record only the fields that changed from the previous revision
  So that revision.json files are minimal and faithfully represent what changed at each point in time

  Background:
    Given the source project is configured for export
    And the export records only the fields that changed between consecutive revisions

  @tfs-object-model
  Scenario: First revision captures all fields because there is no previous revision
    Given work item 1 has its first revision with fields System.Title, System.State, and System.AssignedTo set
    When the export processes revision 0 of work item 1 with no previous revision to compare against
    Then the exported revision contains all three fields: System.Title, System.State, and System.AssignedTo

  @tfs-object-model
  Scenario: Subsequent revision captures only the fields that differ from the previous revision
    Given revision 0 of work item 2 set System.Title to "Initial" and System.State to "New"
    And revision 1 changes System.State to "Active" but leaves System.Title unchanged
    When the export processes revision 1 of work item 2
    Then the exported revision contains System.State with value "Active"
    And the exported revision does not contain System.Title

  @tfs-object-model
  Scenario: Revision with no field changes produces an empty fields collection
    Given revision 2 of work item 3 differs from revision 1 only by a link addition with no field changes
    When the export processes revision 2 of work item 3
    Then the exported revision's fields collection is empty

  @tfs-object-model
  Scenario: Field mapping records both the reference name and the display value
    Given revision 1 of work item 4 changes the field with reference name "System.State" to value "Resolved"
    When the export processes revision 1 of work item 4
    Then the exported field entry has referenceName "System.State"
    And the exported field entry has the display value "Resolved"

  @tfs-object-model
  Scenario: Field values are compared by value equality not reference equality
    Given revision 0 of work item 5 sets a custom integer field "Custom.Priority" to 2
    And revision 1 sets "Custom.Priority" to the same value 2
    When the export processes revision 1 of work item 5
    Then the exported revision does not contain "Custom.Priority" because the value is unchanged

  @tfs-object-model
  Scenario: A field whose value becomes null in a revision is captured as a change
    Given revision 0 of work item 6 has System.AssignedTo set to "user@example.com"
    And revision 1 clears System.AssignedTo so it is null
    When the export processes revision 1 of work item 6
    Then the exported revision contains System.AssignedTo with a null value

  @tfs-object-model
  Scenario: ChangedDate is always captured on every revision regardless of field delta
    Given any revision of work item 7
    When the export processes that revision
    Then the exported revision records the ChangedDate from the source revision
