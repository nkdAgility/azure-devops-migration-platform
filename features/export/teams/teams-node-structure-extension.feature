Feature: Teams NodeTranslation Extension
  As a platform operator
  I want the Teams module to record all team area and iteration paths into the ReferencedPathTracker
  So that NodesModule can pre-create those nodes on the target before teams are imported

  Background:
    Given a simulated source project with teams having area and iteration path assignments

  @export @teams @nodes
  Scenario: Export records team area paths into ReferencedPathTracker
    Given a team with area paths "ProjectA", "ProjectA\\Frontend", "ProjectA\\Backend"
    When the Teams module exports the team
    Then the ReferencedPathTracker contains all three area paths
    And duplicate paths appear only once

  @export @teams @nodes
  Scenario: Export records team iteration paths into ReferencedPathTracker
    Given a team with iteration path "ProjectA\\Sprint 1"
    When the Teams module exports the team
    Then the ReferencedPathTracker contains "ProjectA\\Sprint 1" as an iteration path

  @import @teams @nodes
  Scenario: Import translates area paths via NodeTransformTool
    Given a team package with area path "SourceProject\\Team Area"
    And a NodeTransformTool mapping "SourceProject" → "TargetProject"
    When the Teams module imports the team
    Then SetAreaPathsAsync is called with "TargetProject\\Team Area"

  @export @teams @nodes
  Scenario: NodeTranslation extension disabled suppresses path recording
    Given the NodeTranslation extension is disabled in TeamsModuleOptions
    And a team with area paths "ProjectA\\TeamArea"
    When the Teams module exports the team
    Then the ReferencedPathTracker does not receive any area paths from this team
