Feature: Export Classification Tree
  As a migration operator
  I want the source classification tree to be captured in the package
  So that area and iteration nodes can be recreated on the target

  Scenario: Classification tree is written to the package on export
    Given the source project has area nodes "Area1" and "Area1/Sub1"
    And the source project has iteration nodes "Sprint 1" and "Sprint 2"
    When the NodesModule ExportAsync runs
    Then "Nodes/source-tree.json" is written to the package
    And it contains area path "Area1"
    And it contains iteration path "Sprint 1"

  Scenario: Source tree export is delegated to IClassificationTreeCapture
    Given the NodesModule is configured with Enabled = true
    When ExportAsync is called
    Then IClassificationTreeCapture.CaptureAsync is invoked exactly once

  Scenario: Source tree normalises localised root names to English
    Given the source project uses German locale with root "Bereich" and "Iteration"
    When the classification tree is captured
    Then the root nodes in source-tree.json are "Area" and "Iteration"
