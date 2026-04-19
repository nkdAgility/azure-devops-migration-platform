# Research Findings: System Test Framework for Inventory Command

## Decision: MSTest with `[TestCategory("SystemTest")]` Pattern
**Rationale**: MSTest already used throughout the codebase with established patterns. `[TestCategory("SystemTest")]` provides clean test filtering via `dotnet test --filter "TestCategory=SystemTest"`. `Assert.Inconclusive()` provides graceful handling of missing environment configuration.
**Alternatives considered**: xUnit traits, custom test attributes, or separate test projects. Rejected because they would introduce inconsistency with existing MSTest infrastructure and require additional tooling configuration.

## Decision: Environment Variable Configuration with TokenResolver Integration
**Rationale**: Leverage existing `TokenResolver.Resolve()` pattern for consistent token handling. Environment variables (`AZDEVOPS_SYSTEM_TEST_ORG`, `AZDEVOPS_SYSTEM_TEST_PAT`) provide secure, configurable approach that works in both local dev and CI environments.
**Alternatives considered**: Configuration files, Azure Key Vault, or hardcoded test values. Rejected because config files can accidentally commit secrets, Key Vault adds complexity for simple tests, and hardcoded values prevent flexible testing.

## Decision: Azure DevOps REST API with Basic Authentication (PAT)
**Rationale**: Personal Access Tokens provide simplest, most reliable authentication method for system tests. Compatible with both Azure DevOps Services and Server. Minimal permission scopes possible (Project and Team: Read).
**Alternatives considered**: OAuth 2.0, service principals, or Azure Active Directory. Rejected because OAuth adds complexity for testing scenarios, service principals require Azure subscription dependency, and AAD integration is organization-specific.

## Decision: Rate Limiting with Exponential Backoff and Circuit Breaker
**Rationale**: Azure DevOps has rate limits (200 requests/minute for PATs). Exponential backoff (1s, 2s, 4s, 8s) with maximum 3 retries prevents test flakiness. Circuit breaker pattern fails fast when service is consistently unavailable.
**Alternatives considered**: Linear retry, infinite retry, or no retry logic. Rejected because linear retry wastes time, infinite retry can hang CI, and no retry makes tests fragile to transient network issues.

## Decision: GitHub Actions with Repository Secrets and Conditional Execution
**Rationale**: Repository secrets provide secure environment variable injection. Conditional execution (`if: github.ref == 'refs/heads/main'`) prevents exposing secrets on forks and PRs. Separate test result reporting provides clear system vs unit test visibility.
**Alternatives considered**: Environment-based secrets, workflow secrets, or exposing secrets to all runs. Rejected because environment secrets are less flexible, workflow secrets don't support repo-level sharing, and exposing secrets to forks creates security vulnerability.

## Decision: Test Infrastructure with Existing CLI Patterns and Temporary Cleanup
**Rationale**: Extend existing `InventoryCommandTests.cs` with system test methods to leverage established DI patterns, mocking infrastructure, and test utilities. Temporary directory creation/cleanup ensures no test pollution.
**Alternatives considered**: Separate system test project, integration test framework, or in-memory testing. Rejected because separate project duplicates infrastructure, integration frameworks add complexity, and in-memory testing doesn't validate real Azure DevOps connectivity.

## Decision: Comprehensive Documentation with Troubleshooting Guide
**Rationale**: System tests require environment setup knowledge. Comprehensive `docs/contributors.md` with step-by-step local dev setup, CI configuration, and troubleshooting guide reduces onboarding friction and support burden.
**Alternatives considered**: Inline code comments, wiki documentation, or minimal README additions. Rejected because code comments aren't discoverable for setup, wikis get out of sync, and minimal docs leave gaps for new contributors.

## Decision: Test Categorization Hierarchy
**Rationale**: Use `[TestCategory("SystemTest")]` as primary category with optional secondary categories like `[TestCategory("RequiresAzureDevOps")]` for more granular filtering. Supports CI pipeline stages: Unit → Integration → System tests.
**Alternatives considered**: Single flat categorization or complex multi-dimensional attributes. Rejected because flat categorization lacks flexibility and complex attributes make filtering difficult.

## Decision: Error Handling with Detailed Context
**Rationale**: Comprehensive error messages with actionable guidance. Include error codes, suggested remediation steps, and links to troubleshooting documentation. `Assert.Inconclusive()` for environment issues, `Assert.Fail()` for logic errors.
**Alternatives considered**: Generic error messages or exception propagation. Rejected because generic messages don't help developers diagnose issues and raw exceptions don't provide context for environmental problems.

## Decision: Performance Goals and Constraints
**Rationale**: Target 30-second maximum execution time for basic validation scenarios. Use lightweight API calls (projects list, organization info) rather than full inventory operations. Implement test timeouts to prevent CI hangs.
**Alternatives considered**: Full integration tests or micro-benchmark tests. Rejected because full integration tests take too long for regular CI and micro-benchmarks don't validate real connectivity.

## Decision: Security Patterns
**Rationale**: Never log credential values, use environment variable validation, implement token scoping with minimal permissions (Project and Team: Read only), and provide clear security guidance in documentation.
**Alternatives considered**: Token encryption, credential vaults, or relaxed permissions. Rejected because encryption adds complexity for testing scenarios, vaults require additional infrastructure, and broad permissions violate security principles.