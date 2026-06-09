# Extraction Summary

All scenario intent extracted from feature file:

1. **Second agent is hard-bounced** — AcquireLockAsync throws PackageLockConflictException with owner agent ID when ControlPlane reports owner as Active.
2. **Stale lock replaced** — When ControlPlane reports stale agent as not found, AcquireLockAsync replaces the lock file and succeeds.
3. **Lock released on dispose** — Disposing the lock handle deletes the lock file.
