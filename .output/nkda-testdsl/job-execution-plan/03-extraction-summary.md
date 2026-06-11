# Extraction Summary: job-execution-plan

## Scenarios Extracted
All 4 scenarios extracted from `features/platform/job-execution-plan.feature`.

## Behaviour Mapping
- GET /jobs/{id}/bootstrap returns `JobBootstrap.Tasks` populated after agent pushes plan via POST /agents/lease/{leaseId}/tasks
- GET /jobs/{id}/bootstrap returns Tasks=null before agent pushes plan
- GET /jobs/{id}/tasks returns 200+JobTaskList when plan exists
- GET /jobs/{id}/tasks returns 204 when no plan pushed

## No Step Bindings Found
Feature was unwired — no Reqnroll step definitions existed in tests/.
