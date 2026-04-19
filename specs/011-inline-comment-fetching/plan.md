# Implementation Plan Document

**Feature:** Inline Comment Fetching for Edit/Delete Revisions  
**Module:** WorkItems Export  
**Status:** Specification Complete, Implementation Deferred  

---

## Current State

✅ **Completed:**
- Problem analysis: Dual-channel comment architecture identified
- Architecture documented: System.History vs Comments API
- Root cause found: Comment edits/deletes invisible in revision field data

❌ **Deferred:**
- No code implementation
- No factory injection
- No comment fetching logic

---

## Problem Summary

Azure DevOps work item comments use two parallel channels:

1. **System.History** (Revision Field) — Contains comment **additions** only
   - Already captured in `revision.json`
   - No API call required for additions

2. **Comments API** (Separate Endpoint) — Full version history
   - Edits and deletes invisible in revision fields (only CommentCount changes)
   - Requires separate API call to fetch
   - Currently NOT integrated into export

**Symptom:** Comment edits/deletes are lost during export. Only additions (in System.History) are captured.

---

## Why Implementation is Deferred

### Upstream Blocker
`AzureDevOpsWorkItemCommentSource.GetCommentsAsync()` has bugs in its SDK parameter mapping:
- Incorrect `$top` parameter passed to Azure DevOps REST API v7.1-preview.4
- Causes API error: "A query parameter specified in the request URI is outside the permissible range: $top"
- Blocks any inline comment fetching until fixed

### Low Priority
- Comment additions ARE captured (via System.History)
- Edit/delete history is supplemental enhancement
- No customer-facing feature gap for current export scope

---

## Planned Implementation (When Unblocked)

