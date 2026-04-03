Feature: Control Plane Progress Sink
  As a platform operator
  I want the Migration Agent to stream ProgressEvents to the Control Plane in real time
  So that I can observe job progress without waiting for the job to complete

  @us2 @p1 @progress @sink
  Scenario: Sink POSTs a ProgressEvent to the Control Plane within 1 second of Emit
    Given a Control Plane endpoint is accepting POST requests at "/agents/lease/{leaseId}/progress"
    And the agent holds an active lease
    When the job engine calls Emit with a ProgressEvent
    Then the event is POSTed to the Control Plane endpoint within 1 second
    And the HTTP response status is 204

  @us2 @p1 @progress @sink
  Scenario: Fresh ring buffer is created on Control Plane restart when agent resumes posting
    Given the Control Plane has been restarted and holds no stored events for the lease
    And the agent holds an active lease
    When the job engine calls Emit with a ProgressEvent after the restart
    Then the Control Plane creates a new ring buffer for the job
    And the event is stored successfully

  @us2 @p1 @progress @sink
  Scenario: Transient HTTP failure causes event to be dropped and job continues
    Given the Control Plane endpoint is temporarily unreachable
    And the agent holds an active lease
    When the job engine calls Emit with a ProgressEvent
    Then the event is dropped without throwing an exception
    And a debug-level log entry is emitted
    And subsequent Emit calls are unaffected
