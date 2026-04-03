Feature: Progress Controller
  As a platform operator
  I want to retrieve ProgressEvents for a running job via the Control Plane REST API
  So that I can inspect job progress programmatically

  @us2 @p1 @progress @controller
  Scenario: ProgressEvent is retrievable immediately via GET /jobs/{jobId}/logs
    Given a ProgressEvent has been POSTed for job "22222222-2222-2222-2222-222222222222"
    When I send GET /jobs/22222222-2222-2222-2222-222222222222/logs
    Then the response status is 200
    And the response body contains the stored ProgressEvent

  @us2 @p1 @progress @controller
  Scenario: 404 is returned when the lease is not recognised
    Given there is no active lease for lease id "unknown-lease"
    When the agent POSTs a ProgressEvent to /agents/lease/unknown-lease/progress
    Then the response status is 404

  @us2 @p1 @progress @controller
  Scenario: 403 is returned when caller lacks job visibility
    Given a job "33333333-3333-3333-3333-333333333333" exists in the store
    But the caller does not have permission to view it
    When I send GET /jobs/33333333-3333-3333-3333-333333333333/logs
    Then the response status is 403
