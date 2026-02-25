# Architecture Overview

> This document is the source of truth for design decisions and non-negotiables.
> In any conflict between this document and other sources, this document wins.

## 1. System Purpose

Build a migration package platform, not just a migration tool.

The system supports three modes:

1. **Export** — Azure DevOps Services → Files, or TeamFoundationServer (via .NET 4 OM exporter) → Files
2. **Import** — Files → Azure DevOps Services
3. **Both** — Export → Import in a single orchestrated run

The Files layer is first-class. It is:

- Portable
- Auditable
- Zip-friendly
- Resumable
- Stream-importable
- Human-readable

## 13. What This System Is Now

It is **no longer**:

- A live migration tool
- A direct source-to-target copier

It **is**:

> A versioned migration package platform with streaming chronological replay.

Key properties:

- Deterministic
- Resumable
- Portable
- Auditable
- Extensible
- Pluggable
- Scalable
- Memory-safe for large datasets

## 14. Implementation Priority

1. ArtefactStore (filesystem)
2. StateStore (cursor-based)
3. Manifest & schema
4. WorkItems module (REST)
5. Identity module
6. Legacy TFS export adapter
7. Teams / Permissions / Builds modules

---

*For the full reference set, see [/docs](./) and the agent guardrails in [/agents](../agents/).*
