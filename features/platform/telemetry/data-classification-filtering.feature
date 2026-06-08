Feature: Data classification log filtering
  As a platform operator
  I want customer-identifiable log data to be filtered from Azure Monitor
  So that data sovereignty requirements are met while maintaining full diagnostic logs locally

  Background:
    Given the OpenTelemetry pipeline is configured with the data classification log processor

  Scenario: Customer-classified log still appears in the package log file
    Given a log statement inside a Customer data classification scope
    When the log is written to the package log file
    Then the log record includes a data classification of Customer
    And the log record is present in the package log file
