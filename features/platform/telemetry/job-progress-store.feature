Feature: Job Progress Store
  As a platform operator
  I want the Control Plane to store ProgressEvents in a bounded ring buffer per job
  So that callers can retrieve a recent snapshot without overwhelming memory

  @us2 @p1 @progress @store
  Scenario: Ring buffer at capacity evicts oldest event and stores new event
    Given a JobProgressStore with a capacity of 3
    And the ring buffer for job "11111111-1111-1111-1111-111111111111" is full with 3 events
    When a new ProgressEvent is appended for that job
    Then the oldest event is evicted
    And the ring buffer contains exactly 3 events
    And the newest event is present in the snapshot

  @us2 @p1 @progress @store
  Scenario: CompleteJob called before any Append still marks the job completed so late subscribers get an immediate channel completion
    Given a JobProgressStore with a capacity of 3
    When CompleteJob is called for a job that has no prior events
    And a subscriber connects to that job's SSE stream after CompleteJob
    Then the subscriber's channel is already completed
    And no events are buffered for that job
