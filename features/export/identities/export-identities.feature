Feature: Identities Module Export
  As a migration operator
  I want known source identities to be exported into the package
  So that identity mapping can be performed before import

  @azure-devops-rest @tfs-object-model

  Scenario: Identity export writes known identities to the package
    Given the source project contains users "alice@source.example.com" and "bob@source.example.com"
    When the identities export runs
    Then "Identities/identities.json" is written to the package
    And it contains entries for both "alice@source.example.com" and "bob@source.example.com"
