# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) Naked Agility Limited

@prepare @tfs
Feature: TFS prepare graceful skip

  Scenario: Prepare_TfsSourceOnlyModule_SkipsGracefullyWithWarning
    Given a Team Foundation Server prepare job for a source-only module
    When the prepare phase is executed
    Then the module is skipped gracefully
    And a warning is emitted

