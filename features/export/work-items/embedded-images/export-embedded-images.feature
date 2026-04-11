Feature: Download Embedded Images from Work Item Fields
  As a migration engineer
  I want to download embedded images from work item revisions
  So that the migration package contains self-contained images and is portable across networks

  Background:
    Given the test project is ready for export
    And the WorkItems module is configured with EmbeddedImages.Enabled = true

  Scenario: HTML embedded image is downloaded and URL rewritten
    Given a work item revision contains an HTML field with an embedded ADO image:
      | Field | Value |
      | System.Description | <p>Screenshot: <img src="https://dev.azure.com/org/proj/_apis/wit/attachments/abc123"></p> |
    When the export runs
    Then the image file with SHA-256 derived filename (e.g. abc123def456.png) is written beside revision.json
    And the stored revision.json field value is rewritten to: <p>Screenshot: <img src="abc123def456.png"></p>

  Scenario: Duplicate image URLs within same revision deduplicate
    Given a work item revision contains two fields with the same embedded image URL:
      | Field | Value |
      | System.Description | <img src="https://dev.azure.com/org/proj/_apis/wit/attachments/shared123"> |
      | Microsoft.VSTS.Common.AcceptanceCriteria | <img src="https://dev.azure.com/org/proj/_apis/wit/attachments/shared123"> |
    When the export runs
    Then the image is downloaded once
    And both field values are rewritten to reference the same local filename
    And only one image file is written beside revision.json

  Scenario: External non-ADO image URLs are preserved with warning
    Given a work item revision contains an external image URL:
      | Field | Value |
      | System.Description | <img src="https://example.com/external-image.png"> |
    When the export runs
    Then the image URL is left unchanged in the stored field value
    And a warning is logged: "Could not download image https://example.com/external-image.png, preserving original"

  Scenario: Inaccessible image (HTTP 404) is preserved with warning and export continues
    Given a work item revision contains an ADO image URL that returns 404:
      | Field | Value |
      | System.Description | <img src="https://dev.azure.com/org/proj/_apis/wit/attachments/deleted404"> |
    When the export runs
    Then the original URL is preserved in the stored field value
    And a warning is logged: "Failed to download image..."
    And the export completes successfully without aborting

  Scenario: Markdown embedded images are processed when field format is markdown
    Given a work item revision contains a Markdown field:
      | Field | Format | Value |
      | Custom.MarkdownField | markdown | ![alt text](https://dev.azure.com/org/proj/_apis/wit/attachments/md567) |
    When the export runs
    Then the image is downloaded and the Markdown reference is rewritten to local filename
    And the stored field value is: ![alt text](md567hash.png)
