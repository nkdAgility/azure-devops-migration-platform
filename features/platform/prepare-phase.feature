Feature: Prepare Phase — Identity Discovery and Mapping
  As a migration operator
  I want a prepare phase that discovers target identities and builds mapping
  So that imports fail fast on unresolvable identities before data migration begins

  @TestCategory=UnitTest
  Scenario: Prepare discovers target identities and produces mapping candidates
    Given a package with identity descriptors for 5 users
    And a target system with matching identities for all 5 users
    When the operator runs import with IdentitiesModule enabled
    Then Identities/mapping.json is written to the package
    And all 5 identities are listed as auto-resolved candidates

  @TestCategory=UnitTest
  Scenario: Prepare writes unresolved identities for unmatchable entries
    Given a package with identity descriptors for 3 users
    And a target system with matching identities for only 2 users
    When the operator runs import with IdentitiesModule enabled
    Then Identities/unresolved.json is written to the package
    And unresolved.json contains 1 unmatched identity

  @TestCategory=UnitTest
  Scenario: Import proceeds when all identities are resolved
    Given a package with Identities/descriptors.jsonl containing 3 entries
    And Identities/mapping.json exists with resolved mappings for all 3 entries
    When the IdentitiesModule ImportAsync is called
    Then the import completes without error

  @TestCategory=UnitTest
  Scenario: Import logs warning and continues when descriptors file is missing
    Given a package with no Identities/descriptors.jsonl file
    When the IdentitiesModule ImportAsync is called
    Then a warning is logged indicating the descriptors file is missing
    And the import completes without throwing an exception
