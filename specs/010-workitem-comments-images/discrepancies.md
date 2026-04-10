# Architecture Discrepancies

**Feature**: Work Item Comments and Embedded Images Export
**Flagged by**: speckit.specify
**Status**: Pending rectification (resolve in speckit.implement)

## Discrepancies

### 1. workitems-format.md does not describe `comments.json`

- **Source doc**: `.agents/context/workitems-format.md`
- **Section**: "On-Disk Layout" and "revision.json Required Fields"
- **Issue**: The current layout shows only revision sub-folders and attachments. There is no mention of `comments.json` or its placement in the date folder alongside revision sub-folders, nor of embedded-image files stored beside `comments.json`.
- **Suggested update**: Add a `comments.json` entry to the layout diagram and document its schema (commentId, version, text, format, renderedText, createdBy, createdDate, modifiedBy, modifiedDate, isDeleted). Note that it lives at the date-folder level (e.g. `WorkItems/2026-02-25/<workItemId>-comments.json`), not inside any revision sub-folder.

### 2. workitems-format.md does not describe embedded-image rewriting

- **Source doc**: `.agents/context/workitems-format.md`
- **Section**: "revision.json Required Fields" / "Attachment Rules"
- **Issue**: The current spec documents attachments (files explicitly attached to a revision), but does not address images that are embedded inline inside HTML or Markdown field values. These are a different category: they are downloaded during export and stored beside the revision, and the field value in `revision.json` is rewritten to point to the local file.
- **Suggested update**: Add an "Embedded Images" section describing: (a) how inline images are discovered by scanning HTML `<img src>` and Markdown `![](url)` patterns; (b) that they are downloaded to the same folder as `revision.json` using a deterministic filename; (c) that the stored field value is rewritten to the relative filename; and (d) that images from comments are handled identically but stored alongside `comments.json`.

### 3. package-format.md does not include comments or embedded images in the WorkItems layout

- **Source doc**: `.agents/context/package-format.md`
- **Section**: Package Structure — WorkItems layout diagram
- **Issue**: The canonical layout diagram only shows `revision.json` and `<attachment files>` inside revision sub-folders. It does not show `comments.json` or embedded-image files at the date-folder level.
- **Suggested update**: Update the WorkItems layout diagram to include `<workItemId>-comments.json` at the date-folder level and `<embedded image files>` as a sibling of `revision.json` within revision sub-folders (and beside `comments.json` for comment images).
