Feature: Import Team Members
  As a platform operator
  I want team membership imported with identity mapping
  So that members from the source are correctly added to teams on the target

  Background:
    Given a team package with member list

  # BLOCKED: TeamImportOrchestrator always calls AddMemberAsync with whatever
  # _identityLookupTool.Resolve() returns — there is no skip-on-unresolvable path
  # for members. The LogWarning in the catch block only fires if AddMemberAsync itself
  # throws, not when the identity cannot be resolved.
  # See: src/DevOpsMigrationPlatform.Infrastructure.Agent/Teams/TeamImportOrchestrator.cs:116-135
  @import @teams @members
  Scenario: Unresolvable member identity is skipped with warning
    Given a team package with a member descriptor "src-unknown"
    And the IdentityMappingService returns the default identity for "src-unknown"
    When the Teams module imports the team package
    Then a warning is logged for the unresolvable member
