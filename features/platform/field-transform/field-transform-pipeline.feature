Feature: Field transform pipeline filtering and enabled flags
  As a migration operator
  I want enabled flags and type filters to be respected at every level
  So that I can precisely control which transforms run without removing configuration

  Scenario: Tool-level enabled false prevents all transforms from running
    Given the FieldTransformTool is configured with a SetField transform on "Custom.Field"
    And the tool-level enabled flag is set to false
    When I check whether the tool is enabled for the Import phase
    Then the tool should report it is not enabled

  Scenario: Group-level enabled false skips the entire group
    Given the FieldTransformTool has a disabled group containing a SetField transform on "Custom.Marker"
    And a work item with no fields
    When the field transform pipeline executes via the tool
    Then the field "Custom.Marker" should not be present in the output

  Scenario: Transform-level enabled false skips only that transform
    Given the FieldTransformTool has a group with two SetField transforms on "Custom.One" and "Custom.Two"
    And the second transform is disabled
    And a work item with no fields
    When the field transform pipeline executes via the tool
    Then the field "Custom.One" should have value "set"
    And the field "Custom.Two" should not be present in the output

  Scenario: Configuring a transform targeting an identity field is rejected
    Given a transform factory
    When I try to create a SetField transform targeting "System.CreatedBy"
    Then the factory should throw an identity field exception
