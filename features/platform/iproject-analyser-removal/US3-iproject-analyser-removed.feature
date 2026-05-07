# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) Naked Agility Limited

@icapture @iproject-analyser-removal @platform
Feature: IProjectAnalyser removed from the solution

  Scenario: Solution_AfterRefactor_ContainsNoIProjectAnalyserReferences
    Given the ICapture interface refactor is complete
    When the solution is built
    Then IProjectAnalyser.cs does not exist in any project
    And no source file or test file references the IProjectAnalyser type

  Scenario: DependencyAnalyser_ClassDeclaration_ImplementsOnlyIOrganisationsAnalyser
    Given the refactored DependencyAnalyser class
    When its interface list is inspected
    Then it implements IOrganisationsAnalyser
    And it does not implement IProjectAnalyser
    And it does not implement any per-project capture interface
