# Zip Packaging

## 12. Zip Packaging

Zip is an outer concern. The package format itself is defined by the directory layout; zip is simply a transport wrapper.

### Pack

```
PackageRoot/ → migration-package.zip
```

- Pack is applied after export completes (or on demand via tooling).
- The zip preserves the full relative directory structure from `PackageRoot/` downward.
- No transformation of content occurs during packing; filenames and paths are unchanged.

### Unpack

```
migration-package.zip → PackageRoot/
```

- Unpack extracts to a specified directory before import begins.
- If `artefacts.zip` is `true` in configuration, unpack is handled automatically by the runner before import.
- Partial extraction (e.g., extracting only `.migration/Checkpoints/` for cursor inspection) is a supported tooling use case.

### Guarantees

Because the WorkItems layout is deterministic (lexicographic folder names derived from ticks and IDs), the following guarantees hold:

- **Order preservation:** A zip file unpacked to any platform produces the same folder traversal order.
- **Streaming import compatibility:** Streaming import works identically on a packed-then-unpacked package as on an in-place export.
- **Determinism:** Given the same export input, two independent runs produce structurally identical packages (excluding `runId`, timestamps in `manifest.json`, and `.migration/Logs/`).

### What Zip Does Not Affect

- Cursor state (`.migration/Checkpoints/`) is included in the zip and resumes correctly after unpack.
- ID maps (`.migration/Checkpoints/idmap.db` or `idmap.json`) are included.
- Identity descriptors and mappings (`Identities/`) are included.

### Large Package Considerations

- For packages exceeding typical zip limits (>4 GB), use zip64 format.
- The pack/unpack tooling must explicitly enable zip64.
- Streaming import does not require the full package to be unpacked before starting; per-module extraction is a valid optimisation but is not required by the core design.
