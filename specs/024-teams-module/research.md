# Research — IdentitiesModule, NodeStructureModule & TeamsModule

**Phase 0 Output** — documents API surface research and resolved unknowns.

> **Status**: Placeholder — to be populated during Phase 0 implementation (tasks T000a–T000g).

---

## 1. TFS Identity API Surface

**Task**: T000a  
**Question**: Which TFS OM APIs are available for identity enumeration? Does `IIdentityManagementService2.ReadIdentities()` work for both users and groups?

**Findings**: *(To be completed)*

---

## 2. TFS Teams API Surface

**Task**: T000b  
**Question**: Confirm `TfsTeamService.QueryTeams()`, `GetTeamMembers()`, team settings access. Document version-specific limitations.

**Findings**: *(To be completed)*

---

## 3. ADO Teams REST API Endpoints

**Task**: T000c  
**Question**: Confirm the REST API surface for teams.

**Expected endpoints**:
- `_apis/projects/{project}/teams` — list teams
- `_apis/work/teamsettings` — team settings (backlog levels, working days, bug behaviour)
- `_apis/work/teamsettings/iterations` — team iteration assignments
- `_apis/work/teamsettings/iterations/{id}/capacities` — per-iteration capacity
- `_apis/projects/{project}/teams/{team}/members` — team membership
- `_apis/work/teamsettings/teamfieldvalues` — team area path assignments

**Findings**: *(To be completed)*

---

## 4. ADO Identity REST API

**Task**: T000d  
**Question**: Confirm Graph API or Identity Picker API for enumerating all project identities.

**Findings**: *(To be completed)*

---

## 5. Default Team Detection

**Task**: T000e  
**Question**: How to programmatically identify the project's default team.

**Expected mechanisms**:
- **ADO REST**: `GET _apis/projects/{project}/teams` returns `isTeamDefault` flag per team
- **TFS OM**: `TfsTeamService` — check for default team property or use project-scoped query

**Findings**: *(To be completed)*

---

## 6. Team Area Paths API

**Task**: T000f  
**Question**: Confirm REST API for team area path assignments.

**Expected endpoint**: `_apis/work/teamsettings/teamfieldvalues` returns `defaultValue` and `values[]` with `value` (area path) and `includeChildren` flag.

**Findings**: *(To be completed)*
