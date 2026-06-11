# DSL Design: module-isolation

## Approach
Pure reflection-based unit tests — no external dependencies, no mocks required.

## Test class
`ModuleIsolationTests` in `DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Modules`

## Method mapping

| Scenario | Method |
|----------|--------|
| ModuleConstructed_IsolatedOptions_NoFullGraph | `WorkItemsModule_Constructor_ReceivesIsolatedOptionsSlice_NotFullGraph` |
| ModuleUnitTest_IsolatedOptions_MinimalDependencies | `WorkItemsModule_SourceFile_DoesNotReferenceOtherModuleOptionsTypes` |
| DuplicateSectionName_DIRegistration_FailsAtStartup | `AllModuleOptions_SectionNames_AreUnique` |
| NewModule_FollowsPattern_ExplicitContract | `AllModuleOptions_HaveStaticSectionName` |

## Hygiene
All methods carry `[TestCategory("UnitTest")]`.
