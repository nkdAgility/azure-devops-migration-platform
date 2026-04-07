# Architecture Discrepancies

**Feature**: Work Items Export — Azure DevOps via REST API
**Flagged by**: speckit.specify
**Status**: Pending rectification (resolve in speckit.implement)

## Discrepancies

### WorkItemsModule concrete ADO export implementation not yet documented

- **Source doc**: `docs/modules.md`
- **Section**: Module Responsibilities table — `WorkItemsModule` row
- **Issue**: `docs/modules.md` describes `WorkItemsModule` as "High-fidelity work item revision export/import" but does not document the concrete ADO REST API implementation approach (date-window WIQL strategy, `GetRevisionsAsync` batching, attachment streaming).
- **Suggested update**: Add a `### WorkItemsModule — ADO Export` subsection to `docs/modules.md` describing the `AzureDevOpsWorkItemRevisionSource` pattern, the reuse of `WorkItemQueryWindowStrategy`, and the `IAzureDevOpsAttachmentDownloader` service.

### IWorkItemRevisionSource implementation for ADO not referenced in architecture docs

- **Source doc**: `docs/architecture.md`
- **Section**: Components and Responsibilities — Migration Agent row
- **Issue**: `docs/architecture.md` mentions the Job Engine runs modules but does not yet document that source connectors implement `IWorkItemRevisionSource` and that `AzureDevOpsWorkItemRevisionSource` is the first concrete implementation.
- **Suggested update**: Add a brief note in the Migration Agent component description that "source connectors implement `IWorkItemRevisionSource` in `DevOpsMigrationPlatform.Infrastructure.AzureDevOps`".

### Attachment download contract not yet defined in any doc

- **Source doc**: `.agents/context/workitems-format.md`
- **Section**: Attachment Rules
- **Issue**: The format doc describes the `attachments` metadata shape and that binaries live beside `revision.json`, but does not specify the attachment download abstraction (`IAzureDevOpsAttachmentDownloader`) or the SHA-256 check requirement.
- **Suggested update**: Add an "Attachment Download Contract" section that specifies streaming download (no `MemoryStream` buffering), SHA-256 verification, and retry policy.
