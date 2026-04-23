Feature: Resumable Batching Cursor
  As a migration operator running discovery or dependency analysis over large projects
  I want interrupted batching iteration to resume from a saved continuation token
  So that I do not lose hours of progress after an interruption

  Background:
    Given the migration platform is configured with resumable batching enabled
    And the checkpoint location is ".migration/Checkpoints/<module>.continuation.json"

  # ── US1: Resume Long-Running Iteration Safely ──────────────────────────

  @resumable-batching @us1
  Scenario: Resume from a saved continuation token
    Given a discovery job has previously processed work items up to ChangedDate "2025-06-15" and WorkItemId 4200
    And a continuation token exists with that position and a matching query fingerprint
    When the job restarts with resume enabled
    Then batching continues from the saved continuation position
    And work items before the saved position are not re-fetched

  @resumable-batching @us1
  Scenario: No continuation token starts from the beginning
    Given no continuation token exists for the module
    When the job runs with resume enabled
    Then batching starts from the earliest date without error
    And a ResumeDecision of "Unavailable" is logged

  @resumable-batching @us1
  Scenario: Completion checkpoint marks end of stream
    Given a discovery job processes all available work items
    When the final batch window is yielded
    Then a completion checkpoint is emitted with Completed set to true
    And the caller can detect end-of-stream on the next resume attempt

  @resumable-batching @us1
  Scenario: Boundary cluster with identical ChangedDate values
    Given 500 work items share the same ChangedDate of "2025-06-15"
    And the continuation token records position at WorkItemId 250
    When the job resumes from that token
    Then all 500 work items are eventually processed
    And no items in the cluster are skipped

  @resumable-batching @us1
  Scenario: Resume with more than 20000 items since saved position
    Given more than 20000 work items exist between the saved continuation date and now
    When the job resumes from the saved token
    Then the window strategy subdivides correctly
    And all items are enumerated without exceeding the WIQL result limit

  # ── US2: Prevent Unsafe Resume After Query Changes ─────────────────────

  @resumable-batching @us2
  Scenario: Query fingerprint mismatch rejects continuation
    Given a saved continuation token with query fingerprint "abc123"
    And the current query produces a different fingerprint "def456"
    When the job attempts to resume
    Then a ResumeRejectedException is thrown with both fingerprints in the payload
    And a ResumeDecision of "RejectedQueryMismatch" is recorded

  @resumable-batching @us2
  Scenario: Query fingerprint match accepts continuation
    Given a saved continuation token with query fingerprint "abc123"
    And the current query produces the same fingerprint "abc123"
    When the job attempts to resume
    Then a ResumeDecision of "Accepted" is returned
    And enumeration begins from the saved position

  @resumable-batching @us2
  Scenario: Pre-check returns decision without starting enumeration
    Given a saved continuation token exists
    When the caller invokes EvaluateResumeDecisionAsync
    Then a ResumeDecision is returned without fetching any work items
    And the decision matches what FetchAsync would produce

  # ── US3: Handle Duplicates and Data Drift Predictably ──────────────────

  @resumable-batching @us3
  Scenario: Deterministic ordering uses ChangedDate ascending then WorkItemId ascending
    Given resume is enabled for a discovery job
    When work items are enumerated
    Then results are ordered by ChangedDate ascending then WorkItemId ascending
    And this ordering is consistent across interrupted and resumed runs

  @resumable-batching @us3
  Scenario: Source drift yields duplicate items without suppression
    Given a discovery job resumes after source work items have been edited
    And some items now appear in multiple query windows due to changed dates
    When the resumed enumeration encounters these overlapping items
    Then duplicate item IDs are yielded to the caller without suppression
    And the caller is responsible for idempotent handling

  @resumable-batching @us3
  Scenario: Resumed run processes all items despite source mutations
    Given a discovery job was interrupted after processing 1000 of 5000 work items
    And 50 of the already-processed items were edited after the interruption
    When the job resumes from the saved continuation token
    Then all 5000 original items plus the 50 re-dated items are eventually processed
    And no items are missed due to resume position logic
