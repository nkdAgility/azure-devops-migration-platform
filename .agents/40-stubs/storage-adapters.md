# Storage Adapters — Directory Rules

Concrete `IArtefactStore`/`IStateStore` implementations live here. They are seam
implementations, not module code.

## ⛔ Blocking rules

1. **`EnumerateAsync` must return strict ascending lexicographic order.** Out-of-order enumeration silently breaks streaming import. No in-memory sorting to compensate elsewhere. (`.agents/20-guardrails/core/architecture-boundaries.md` rule 14)
2. **Translate I/O errors at the seam.** Consumers of `IPackageAccess` must never see `System.IO` exception types — missing meta is an idempotent no-op or `PackageMetaNotFoundException`. (ADR-0025)
3. **Reject path traversal.** Resolved paths must never escape the package root. (`.agents/20-guardrails/domains/security-rules.md` rule 12)
4. **No module, host, or orchestration logic here.** Storage selection belongs to host composition roots only. (ADR-0022)
5. **Local ↔ cloud swap = zero module code changes.** Behavioural parity between FileSystem and AzureBlob is contractual; a capability one store cannot honour is a Class C contract question, not a silent divergence. (`.agents/20-guardrails/core/architecture-boundaries.md` rule 13)
6. Streaming only: binaries via `WriteBinaryAsync`/stream APIs — never materialize whole payloads in memory.

## Authority

- Contracts: `.agents/10-contracts/specs/package-persistence-contract.md`, `.agents/10-contracts/specs/package-boundary-contract.md`
- Rules: `.agents/20-guardrails/core/architecture-boundaries.md`, `.agents/20-guardrails/domains/package-rules.md`
