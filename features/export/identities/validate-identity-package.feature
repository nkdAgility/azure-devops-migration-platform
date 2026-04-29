Feature: Validate Identity Package
  As a migration operator
  I want the IdentitiesModule ValidateAsync to detect malformed packages
  So that import failures are caught early before any data is written to the target

  @TestCategory=UnitTest
  Scenario: Missing descriptors.jsonl produces a validation error
    Given a package with no Identities/descriptors.jsonl file
    When the IdentitiesModule ValidateAsync is called
    Then a validation error is produced for path "Identities/descriptors.jsonl"
    And the error message mentions "missing"

  @TestCategory=UnitTest
  Scenario: Malformed JSONL line produces a validation error
    Given a package with Identities/descriptors.jsonl containing a malformed JSON line
    When the IdentitiesModule ValidateAsync is called
    Then a validation error is produced for path "Identities/descriptors.jsonl"
    And the error message mentions "malformed"

  @TestCategory=UnitTest
  Scenario: Missing required field produces a validation error
    Given a package with Identities/descriptors.jsonl where a line is missing the descriptor field
    When the IdentitiesModule ValidateAsync is called
    Then a validation error is produced for path "Identities/descriptors.jsonl"
    And the error message mentions the missing field

  @TestCategory=UnitTest
  Scenario: Valid descriptors.jsonl with all required fields produces no validation errors
    Given a package with Identities/descriptors.jsonl containing 3 valid entries
    When the IdentitiesModule ValidateAsync is called
    Then no validation errors are produced
