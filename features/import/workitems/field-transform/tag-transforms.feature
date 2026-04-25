Feature: Work item field to tag transforms
  As a migration operator
  I want to transform field values into tags
  So that structured field data can be surfaced as searchable tags in the target system

  Scenario: FieldToTag copies field value as a tag
    Given a work item with field "Custom.Priority" set to "High"
    And a FieldToTag transform is configured for field "Custom.Priority"
    When the field transform pipeline executes
    Then the field "System.Tags" should contain tag "High"

  Scenario: ConditionalTag adds tag when field matches pattern
    Given a work item with field "System.State" set to "Resolved"
    And a ConditionalTag transform is configured to add tag "Closed" when field "System.State" matches "Resolved|Done|Closed"
    When the field transform pipeline executes
    Then the field "System.Tags" should contain tag "Closed"

  Scenario: ConditionalTag skips when field does not match
    Given a work item with field "System.State" set to "Active"
    And a ConditionalTag transform is configured to add tag "Closed" when field "System.State" matches "Resolved|Done"
    When the field transform pipeline executes
    Then the field "System.Tags" should not contain tag "Closed"

  Scenario: MergeToTag deduplicates tags case-insensitively
    Given a work item with field "System.Tags" set to "High; high; MEDIUM"
    And a MergeToTag transform is configured for source fields "System.Tags"
    When the field transform pipeline executes
    Then the field "System.Tags" should have deduplicated tags "High; MEDIUM"

  Scenario: TreeToTag flattens path segments into tags
    Given a work item with field "System.AreaPath" set to "Project\\Team\\Component"
    And a TreeToTag transform is configured for field "System.AreaPath"
    When the field transform pipeline executes
    Then the field "System.Tags" should contain tag "Project"
    And the field "System.Tags" should contain tag "Team"
    And the field "System.Tags" should contain tag "Component"
