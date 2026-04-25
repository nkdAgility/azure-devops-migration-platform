Feature: Work item field batch copying
  As a migration operator
  I want to copy multiple fields at once using a single batch declaration
  So that I can efficiently rename many fields without repeating individual copy rules

  Scenario: Multiple field mappings are copied in a single pass
    Given a work item with field "Custom.OldTitle" set to "My Title" and "Custom.OldPriority" set to "High"
    And a CopyFieldBatch transform is configured with mappings "Custom.OldTitle" -> "Custom.NewTitle" and "Custom.OldPriority" -> "Custom.NewPriority"
    When the field transform pipeline executes
    Then the field "Custom.NewTitle" should have value "My Title"
    And the field "Custom.NewPriority" should have value "High"

  Scenario: Absent source field is silently skipped
    Given a work item without field "Custom.Missing"
    And a CopyFieldBatch transform is configured with mapping "Custom.Missing" -> "Custom.Target"
    When the field transform pipeline executes
    Then the field "Custom.Target" should not be present in the output

  Scenario: Only present fields are copied when batch is partially absent
    Given a work item with field "Custom.FieldA" set to "ValueA"
    And a CopyFieldBatch transform is configured with mappings "Custom.FieldA" -> "Custom.CopyA" and "Custom.Missing" -> "Custom.CopyB"
    When the field transform pipeline executes
    Then the field "Custom.CopyA" should have value "ValueA"
    And the field "Custom.CopyB" should not be present in the output
