Feature: Import Team Area Paths
  As a platform operator
  I want team area path assignments imported with path translation
  So that teams are associated with the correct area paths on the target system

  Background:
    Given a team package with area path assignments

  @import @teams @nodes
  Scenario: Import translates default and included area paths
    Given a team package with default area path "SourceProject" and included paths "SourceProject", "SourceProject\\Sub"
    And a NodeTranslationTool mapping "SourceProject" → "TargetProject"
    When the Teams module imports the team package
    Then SetAreaPathsAsync is called with defaultAreaPath "TargetProject"
    And the included paths contain "TargetProject" and "TargetProject\\Sub"

  @import @teams @nodes
  Scenario: Untranslatable included area path is skipped with warning
    Given a team package with default area path "ProjectA" and an included path "ProjectA\\ObsoleteArea"
    And the NodeTranslationTool returns null for "ProjectA\\ObsoleteArea"
    When the Teams module imports the team package
    Then SetAreaPathsAsync is called without "ProjectA\\ObsoleteArea" in the included paths
    And a warning is logged for the untranslatable path

  @import @teams @nodes
  Scenario: Untranslatable default area path prevents SetAreaPathsAsync from being called
    Given a team package with default area path "UnknownProject"
    And the NodeTranslationTool returns null for "UnknownProject"
    When the Teams module imports the team package
    Then SetAreaPathsAsync is not called for this team

  @import @teams @nodes
  Scenario: NodeStructure extension disabled uses source paths without translation
    Given a team package with area path "SourceProject\\TeamArea"
    And the NodeStructure extension is disabled
    When the Teams module imports the team package
    Then SetAreaPathsAsync is not called (NodeStructure extension disabled)
