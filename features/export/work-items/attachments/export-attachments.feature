Feature: Export Attachments
  As a migration operator
  I want attachments exported alongside their owning revision
  So that each revision folder is self-contained and the package is portable

  @azure-devops-rest @tfs-object-model

  Background:
    Given the source project contains work items with file attachments
    And the export module is configured with valid source credentials

  Scenario: Attachment is stored beside revision.json in the same revision folder
    Given revision 2 of work item 99 has an attachment named "screenshot.png"
    When the WorkItems export module runs
    Then "screenshot.png" is stored at "WorkItems/yyyy-MM-dd/<ticks>-99-2/screenshot.png"
    And "revision.json" in the same folder references "screenshot.png" by relative path

  Scenario: No global Attachments root directory is created
    Given any work item with attachments is exported
    When the WorkItems export module runs
    Then no directory named "Attachments/" exists at the package root
    And no directory named "Attachments/" exists at the "WorkItems/" level

  Scenario: Multiple attachments on the same revision are all stored in the revision folder
    Given revision 1 of work item 7 has 3 attachments: "spec.docx", "design.png", "notes.txt"
    When the WorkItems export module runs
    Then "WorkItems/yyyy-MM-dd/<ticks>-7-1/" contains "spec.docx", "design.png", and "notes.txt"
    And "revision.json" lists all three attachments

  Scenario: Revision without attachments exports only revision.json
    Given revision 0 of work item 55 has no attachments
    When the WorkItems export module runs
    Then "WorkItems/yyyy-MM-dd/<ticks>-55-0/" contains only "revision.json"

  Scenario: Attachment export writes only via IArtefactStore
    Given a revision with an attachment
    When the attachment is exported
    Then the attachment binary is written via IArtefactStore
    And no direct file stream is opened inside the module
