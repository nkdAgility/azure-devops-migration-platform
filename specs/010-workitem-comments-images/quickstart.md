# Quickstart: Work Item Comments and Embedded Images Export

**Feature**: `010-workitem-comments-images`

---

## What It Does

When exporting work items, the platform now:
1. **Exports comments** — calls the separate ADO Comments API and writes one `comment.json` per comment version into `WorkItems/yyyy-MM-dd/<ticks>-<workItemId>-c<commentId>/`
2. **Downloads embedded images** — scans HTML and Markdown fields in both revisions and comments, downloads ADO-hosted images, stores them beside their parent document, and rewrites the field value to use the local filename

---

## Configuration

Add to your scenario JSON:

```json
{
  "job": {
    "modules": {
      "workItems": {
        "scopes": {
          "comments": {
            "enabled": true,
            "includeDeleted": false
          },
          "embeddedImages": {
            "enabled": true,
            "downloadTimeoutSeconds": 30
          }
        }
      }
    }
  }
}
```

All new scope parameters are optional; the defaults shown above apply if omitted.

---

## Expected Output

After running an export on work item #12345 with 2 revisions and 1 comment:

```
WorkItems/
  2026-01-15/
    00638700000000000000-12345-1/       ← revision 1
      revision.json
      a3f9b812c4.png                    ← image from System.Description field

    00638700501911000000-12345-2/       ← revision 2
      revision.json

    00638700501911000000-12345-c45/     ← comment #45
      comment.json
      f7d2a91c88.jpg                    ← image embedded in comment text

Checkpoints/
  workitems.cursor.json                 ← revision resume cursor
  workitems-comments.cursor.json        ← comments resume cursor
```

---

## Sample comment.json

```json
{
  "commentId": 45,
  "version": 1,
  "text": "See diagram: <img src=\"f7d2a91c88.jpg\" />",
  "renderedText": "<p>See diagram: <img src=\"f7d2a91c88.jpg\" /></p>",
  "format": "html",
  "isDeleted": false,
  "createdBy": {
    "displayName": "Jamal Hartnett",
    "uniqueName": "fabrikamfiber4@hotmail.com",
    "descriptor": "aad.YTkzODFkODYt..."
  },
  "createdDate": "2026-01-15T10:23:11Z",
  "modifiedBy": {
    "displayName": "Jamal Hartnett",
    "uniqueName": "fabrikamfiber4@hotmail.com",
    "descriptor": "aad.YTkzODFkODYt..."
  },
  "modifiedDate": "2026-01-15T10:23:11Z"
}
```

The `text` field has the URL already rewritten from the original ADO attachments URL to the local filename.

---

## Image Deduplication Behaviour

If the same ADO image URL appears in two fields within the same revision, only **one** local file is written. Both field values in `revision.json` are rewritten to point to the same filename.

Deduplication is **per parent folder**. The same image appearing in a different revision or comment folder results in a second local copy.

---

## Handling External Images

Images hosted outside the ADO organisation (e.g. `https://example.com/logo.png`) are **preserved as-is** in the field value. A `WARN` level log entry is emitted with the work item ID and original URL — no download is attempted.

---

## Resumability

If an export is interrupted mid-comments, on restart the platform reads `workitems-comments.cursor.json` and skips all work items with ID ≤ `lastProcessedWorkItemId`, then resumes from the next one. Comment folders already written are left intact.

---

## TFS Compatibility

For TFS 2015–2017 (< 2018 Update 2), the Comments API does not exist. The comment export step is skipped automatically; a metrics counter `workitems.comments.skipped_tfs_not_supported` records the count. Everything else (revisions, attachments, image export) continues normally.

---

## Disabling Comments or Images

```json
{
  "job": {
    "modules": {
      "workItems": {
        "scopes": {
          "comments": {
            "enabled": false
          },
          "embeddedImages": {
            "enabled": false
          }
        }
      }
    }
  }
}
```

Setting `comments.enabled: false` skips the Comments API entirely — no comment folders are created.

Setting `embeddedImages.enabled: false` stores field values verbatim with original ADO URLs — no local image download occurs.
