Feature: Export payload complexity metrics
  As a migration operator
  I want to see histograms of field count, attachment count, link count, revision count, and payload bytes per work item
  So that I can identify slow or complex work items driving export duration

  Background:
    Given a migration configuration targeting the Simulated source
    And the configuration specifies operation "export" for module "workitems"

  @simulated
  Scenario: Payload histograms reflect actual work item complexity
    Given a work item with 15 revisions, 3 attachments, and 8 links
    When the work item is exported
    Then the "migration.workitem.revision.count" histogram records a value of 15
    And the "migration.workitem.attachment.count" histogram records a value of 3
    And the "migration.workitem.link.count" histogram records a value of 8

  @simulated
  Scenario: MetricSnapshot shows batch mean values for payload histograms
    Given a migration job in Export mode processing 10 work items with varying complexity
    When the export completes
    Then the MetricSnapshot property "RevisionCountMean" is greater than 0
    And the MetricSnapshot property "FieldCountMean" is greater than 0
    And the MetricSnapshot property "PayloadBytesMean" is greater than 0
