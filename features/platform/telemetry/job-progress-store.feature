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
