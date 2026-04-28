Feature: Import Team Members
  As a platform operator
  I want team membership imported with identity mapping
  So that members from the source are correctly added to teams on the target

  Background:
    Given a team package with member list

  @import @teams @members
  Scenario: Import adds all members using identity mapping
    Given a team package with 2 members: descriptor "src-alice" and "src-bob"
    And an identity mapping: "src-alice" → "tgt-alice@target.com", "src-bob" → "tgt-bob@target.com"
    When the Teams module imports the team package
    Then AddMemberAsync is called twice
    And member descriptors are translated to "tgt-alice@target.com" and "tgt-bob@target.com"

  @import @teams @members
  Scenario: Unresolvable member identity is skipped with warning
    Given a team package with a member descriptor "src-unknown"
    And the IdentityMappingService returns the default identity for "src-unknown"
    When the Teams module imports the team package
    Then a warning is logged for the unresolvable member

  @import @teams @members
  Scenario: Admin flag is preserved after identity mapping
    Given a team package with an admin member descriptor "src-alice"
    And the identity mapping resolves "src-alice" → "tgt-alice@target.com"
    When the Teams module imports the team package
    Then AddMemberAsync is called with isAdmin=true for the resolved member
