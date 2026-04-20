Feature: Data classification log filtering
  As a platform operator
  I want customer-identifiable log data to be filtered from Azure Monitor
  So that data sovereignty requirements are met while maintaining full diagnostic logs locally

  Background:
    Given the OpenTelemetry pipeline is configured with the data classification log processor

  Scenario: Unclassified log is exported to Azure Monitor
    Given a log statement with no data classification scope
    When the log record reaches the OTel pipeline
    Then the log record is exported to Azure Monitor

  Scenario: Customer-classified log is filtered from Azure Monitor
    Given a log statement inside a Customer data classification scope
    When the log record reaches the OTel pipeline
    Then the log record is not exported to Azure Monitor

  Scenario: System-classified log is exported to Azure Monitor
    Given a log statement inside a System data classification scope
    When the log record reaches the OTel pipeline
    Then the log record is exported to Azure Monitor

  Scenario: Derived-classified log is exported to Azure Monitor
    Given a log statement inside a Derived data classification scope
    When the log record reaches the OTel pipeline
    Then the log record is exported to Azure Monitor

  Scenario: Customer-classified log still appears in the package log file
    Given a log statement inside a Customer data classification scope
    When the log is written to the package log file
    Then the log record includes a data classification of Customer
    And the log record is present in the package log file

  Scenario: Nested scope uses innermost classification
    Given a log statement inside a System scope containing an inner Customer scope
    When the log record reaches the OTel pipeline from the inner scope
    Then the log record is not exported to Azure Monitor
