# Data Model — IdentitiesModule, NodeStructureModule & TeamsModule

**Phase 1 Output** — defines JSON schemas for all package artefacts introduced by this spec.

---

## Identities/descriptors.jsonl

JSONL format — one JSON object per line.

```jsonc
// Each line:
{
  "descriptor": "aad.abc123...",        // Unique identity descriptor (AAD object ID, TFS SID, or Simulated ID)
  "displayName": "Jane Doe",            // Display name
  "uniqueName": "jane.doe@contoso.com", // UPN (null for groups or TFS accounts without email)
  "sourceType": "user",                 // "user" | "group"
  "origin": "aad",                      // "aad" | "tfs" | "simulated"
  "isActive": true                      // Whether the identity is active in the source
}
```

---

## Identities/mapping.json

Operator-provided overrides and auto-resolved candidates (written by Prepare phase).

```jsonc
{
  "version": "1.0",
  "mappings": [
    {
      "sourceDescriptor": "aad.abc123...",
      "targetDescriptor": "aad.def456...",
      "resolutionMethod": "manual",       // "manual" | "upn" | "displayName"
      "confidence": 1.0                   // 1.0 for manual/exact, 0.0–1.0 for fuzzy
    }
  ]
}
```

---

## Identities/unresolved.json

Identities that could not be resolved during Prepare.

```jsonc
{
  "version": "1.0",
  "unresolved": [
    {
      "sourceDescriptor": "aad.xyz789...",
      "displayName": "Former Employee",
      "uniqueName": null,
      "reason": "no-upn-match",           // "no-upn-match" | "no-displayname-match" | "target-directory-unavailable"
      "attemptedAt": "2026-04-28T10:00:00Z"
    }
  ]
}
```

---

## Nodes/source-tree.json

Complete classification tree from the source project.

```jsonc
{
  "version": "1.0",
  "projectName": "MyProject",
  "capturedAt": "2026-04-28T10:00:00Z",
  "trees": [
    {
      "type": "Area",                     // "Area" | "Iteration"
      "rootPath": "\\MyProject\\Area",
      "nodes": [
        {
          "path": "\\MyProject\\Area\\Frontend",
          "name": "Frontend",
          "children": [
            {
              "path": "\\MyProject\\Area\\Frontend\\Mobile",
              "name": "Mobile",
              "children": []
            }
          ]
        }
      ]
    },
    {
      "type": "Iteration",
      "rootPath": "\\MyProject\\Iteration",
      "nodes": [
        {
          "path": "\\MyProject\\Iteration\\Sprint 1",
          "name": "Sprint 1",
          "startDate": "2026-01-06T00:00:00Z",
          "endDate": "2026-01-19T00:00:00Z",
          "children": []
        }
      ]
    }
  ]
}
```

---

## Nodes/referenced-paths.json

Accumulated set of actually-referenced paths from all module extensions.

```jsonc
{
  "version": "1.0",
  "areaPaths": [
    "\\MyProject\\Area\\Frontend",
    "\\MyProject\\Area\\Frontend\\Mobile"
  ],
  "iterationPaths": [
    "\\MyProject\\Iteration\\Sprint 1",
    "\\MyProject\\Iteration\\Sprint 2"
  ]
}
```

---

## Teams/{team-slug}/team.json

Complete team artefact — one file per team.

```jsonc
{
  "version": "1.0",
  "name": "Frontend Team",               // Original display name
  "slug": "frontend-team",               // Filesystem-safe slug
  "description": "The frontend dev team",
  "isDefault": false,                     // true for the project's default team
  "settings": {
    "backlogNavigationLevels": {
      "Epics": true,
      "Features": true,
      "Stories": true
    },
    "workingDays": ["monday", "tuesday", "wednesday", "thursday", "friday"],
    "bugsBehavior": "asRequirements",     // "asRequirements" | "asTasks" | "off"
    "defaultIterationPath": "\\MyProject\\Iteration\\Sprint 1",
    "defaultAreaPath": "\\MyProject\\Area\\Frontend"
  },
  "iterations": [
    {
      "path": "\\MyProject\\Iteration\\Sprint 1",
      "startDate": "2026-01-06T00:00:00Z",
      "endDate": "2026-01-19T00:00:00Z"
    }
  ],
  "members": [
    {
      "identityDescriptor": "aad.abc123...",
      "displayName": "Jane Doe",
      "isAdmin": false
    }
  ],
  "capacity": [
    {
      "iterationPath": "\\MyProject\\Iteration\\Sprint 1",
      "entries": [
        {
          "identityDescriptor": "aad.abc123...",
          "activities": [
            { "name": "Development", "capacityPerDay": 6.0 }
          ],
          "daysOff": [
            { "start": "2026-01-13T00:00:00Z", "end": "2026-01-13T00:00:00Z" }
          ]
        }
      ]
    }
  ],
  "areaPaths": {
    "defaultPath": "\\MyProject\\Area\\Frontend",
    "paths": [
      {
        "path": "\\MyProject\\Area\\Frontend",
        "includeSubAreas": true,
        "isDefault": true
      }
    ]
  }
}
```

---

## Cursor Schemas

### .migration/Checkpoints/identities.cursor.json

```jsonc
{
  "version": "1.0",
  "phase": "export",                      // "export" | "prepare" | "import"
  "lastProcessedDescriptor": "aad.abc123...",
  "totalProcessed": 42,
  "completedAt": null                     // ISO 8601 timestamp when phase completed
}
```

### .migration/Checkpoints/nodes.cursor.json

```jsonc
{
  "version": "1.0",
  "phase": "import",                      // "export" | "import"
  "lastProcessedPath": "\\MyProject\\Area\\Frontend\\Mobile",
  "nodesCreated": 15,
  "nodesSkipped": 3,                      // Already existed on target
  "completedAt": null
}
```

### .migration/Checkpoints/teams.cursor.json

```jsonc
{
  "version": "1.0",
  "phase": "export",                      // "export" | "import"
  "lastProcessedTeam": "frontend-team",   // Team slug
  "lastCompletedExtension": "TeamIterations",  // Last extension that completed for this team
  "teamsProcessed": 3,
  "completedAt": null
}
```
