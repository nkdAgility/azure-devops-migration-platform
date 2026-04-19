# Data Model: Work Item Comments and Embedded Images Export

**Feature**: `010-workitem-comments-images`  
**Date**: 2026-04-10

---

## Package Layout Extension

```
WorkItems/
  yyyy-MM-dd/
    <ticks>-<workItemId>-<revisionIndex>/          ← existing revision folder
      revision.json                                ← existing (field values REWRITTEN to local paths)
      <sha256>.<ext>                               ← NEW: embedded images from revision fields

    <ticks>-<workItemId>-c<commentId>/             ← NEW: comment folder
      comment.json                                 ← NEW: single comment version record
      <sha256>.<ext>                               ← NEW: embedded images from comment text

Checkpoints/
  workitems.cursor.json                            ← existing (revisions)
  workitems-comments.cursor.json                   ← NEW: comments resume cursor
```

**Folder naming — comment:** `<ticks>-<workItemId>-c<commentId>/`
- `ticks` = `createdDate.Ticks` (20-digit, D20 format) for original; `modifiedDate.Ticks` for edit versions
- `c` prefix before `commentId` distinguishes comment folders from revision folders in lexicographic enumeration
- Inner file named `comment.json` (vs `revision.json`) — dual type signal (folder name + file name)

**BuildCommentFolderPath helper:**
```csharp
public static string BuildCommentFolderPath(int workItemId, int commentId, DateTimeOffset date)
{
    var datePart = date.ToString("yyyy-MM-dd");
    var ticks = date.Ticks.ToString("D20");
    return $"WorkItems/{datePart}/{ticks}-{workItemId}-c{commentId}/";
}
```

---

## New Records (Abstractions)

### WorkItemComment

```csharp
/// <summary>
/// Represents one version of a work item comment as stored in comment.json.
/// </summary>
public record WorkItemComment
{
    public int CommentId { get; init; }
    public int Version { get; init; }
    public string Text { get; init; } = string.Empty;     // raw text (may be HTML or Markdown)
    public string? RenderedText { get; init; }             // HTML rendering when format=markdown
    public string Format { get; init; } = "html";          // "html" | "markdown"
    public bool IsDeleted { get; init; }
    public WorkItemIdentityRef CreatedBy { get; init; } = new();
    public DateTimeOffset CreatedDate { get; init; }
    public WorkItemIdentityRef ModifiedBy { get; init; } = new();
    public DateTimeOffset ModifiedDate { get; init; }
}

public record WorkItemIdentityRef
{
    public string DisplayName { get; init; } = string.Empty;
    public string UniqueName { get; init; } = string.Empty;
    public string Descriptor { get; init; } = string.Empty;
}
```

### EmbeddedImageRef

```csharp
/// <summary>
/// Tracks a single embedded image URL found in a field value or comment text,
/// and the local filename it was downloaded to.
/// </summary>
public record EmbeddedImageRef
{
    public string OriginalUrl { get; init; } = string.Empty;
    public string LocalFileName { get; init; } = string.Empty;  // sha256.ext
    public bool Downloaded { get; init; }
    public string? FailureReason { get; init; }
}
```

---

## New Interfaces (Abstractions)

### IWorkItemCommentSource

```csharp
/// <summary>
/// Retrieves all comment versions for a single work item from the source system.
/// Each call returns the complete set of comment folders to write.
/// </summary>
public interface IWorkItemCommentSource
{
    /// <summary>
    /// Streams all comment records (all versions of all comments) for <paramref name="workItemId"/>.
    /// The source returns them in ascending chronological order (createdDate/modifiedDate).
    /// </summary>
    IAsyncEnumerable<WorkItemComment> GetCommentsAsync(
        int workItemId,
        bool includeDeleted,
        CancellationToken cancellationToken);
}
```

### IEmbeddedImageDownloader

```csharp
/// <summary>
/// Downloads an ADO-hosted image URL and returns the raw bytes plus the inferred extension.
/// Returns null if the URL is not hosted on the source organisation, or if the download fails.
/// </summary>
public interface IEmbeddedImageDownloader
{
    /// <summary>
    /// Attempts to download the image. Returns null on non-ADO URLs or failures.
    /// Emits a warning log entry on failure; does not throw.
    /// </summary>
    Task<EmbeddedImageDownloadResult?> TryDownloadAsync(
        string imageUrl,
        CancellationToken cancellationToken);
}

public record EmbeddedImageDownloadResult
{
    public required byte[] Bytes { get; init; }
    public required string Extension { get; init; }
}
```

### IWorkItemCommentExportService

```csharp
/// <summary>
/// Exports all comment versions for a single work item by calling
/// <see cref="IWorkItemCommentSource"/> and writing the resulting
/// <c>comment.json</c> files via <c>IArtefactStore</c>.
/// </summary>
public interface IWorkItemCommentExportService
{
    /// <summary>
    /// Exports all comment versions for <paramref name="workItemId"/>.
    /// </summary>
    Task ExportAsync(int workItemId, CancellationToken cancellationToken);
}
```

### IEmbeddedImageExportService

```csharp
/// <summary>
/// Processes HTML or Markdown content, extracting ADO-hosted image URLs,
/// downloading each image, and rewriting URLs to local filenames.
/// </summary>
public interface IEmbeddedImageExportService
{
    /// <summary>
    /// Processes HTML content, downloading all ADO-hosted images and rewriting
    /// their <c>src</c> attributes to local filenames.
    /// </summary>
    Task<string> ProcessHtmlAsync(string html, string folderPath, CancellationToken cancellationToken);

    /// <summary>
    /// Processes Markdown content, downloading all ADO-hosted images and rewriting
    /// their <c>![](url)</c> references to local filenames.
    /// </summary>
    Task<string> ProcessMarkdownAsync(string markdown, string folderPath, CancellationToken cancellationToken);
}
```

---

## Comments Cursor Schema

```json
{
  "lastProcessedWorkItemId": 12345,
  "stage": "Completed",
  "updatedAt": "2026-04-10T12:00:00Z"
}
```

`stage` values for comments cursor:
| Value | Meaning |
|---|---|
| `FetchedComments` | Comments fetched from API; comment.json files not yet written |
| `Completed` | All comment folders written; images downloaded |

---

## Configuration — WorkItemsScopeParameters and CommentsScope Extensions

**New type: `CommentsScope`**

```csharp
/// <summary>
/// Configuration for work item comments export sub-module.
/// </summary>
public sealed class CommentsScope
{
    public const string SectionName = "Comments";

    /// <summary>
    /// When <c>true</c>, comment versions are fetched and exported.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// When <c>true</c>, soft-deleted comments (<see cref="WorkItemComment.IsDeleted"/> = true) are included.
    /// </summary>
    public bool IncludeDeleted { get; init; } = false;
}
```

**Extension: `IEmbeddedImagesScope`**

```csharp
/// <summary>
/// Configuration for embedded image download sub-module.
/// </summary>
public sealed class EmbeddedImagesScope
{
    public const string SectionName = "EmbeddedImages";

    /// <summary>
    /// When <c>true</c>, embedded ADO-hosted images are downloaded.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// HTTP timeout in seconds for downloading a single image.
    /// </summary>
    public int DownloadTimeoutSeconds { get; init; } = 30;
}
```

**Extension to `WorkItemsScopeParameters`**

```csharp
// Added to existing WorkItemsScopeParameters:
public CommentsScope Comments { get; init; } = new();
public EmbeddedImagesScope EmbeddedImages { get; init; } = new();
```

**Scenario config key mapping:**

```json
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
```

---

## Embedded Image Rewrite Example

Before (stored in `revision.json` today):
```json
{ "referenceName": "System.Description", "value": "<div><img src=\"https://dev.azure.com/fabrikam/proj/_apis/wit/attachments/abc-123?fileName=diagram.png\" /></div>" }
```

After (stored in `revision.json` with this feature):
```json
{ "referenceName": "System.Description", "value": "<div><img src=\"a3f9b812...c4.png\" /></div>" }
```

Corresponding image file on disk: `WorkItems/2026-01-15/638700000000-12345-2/a3f9b812...c4.png`

---

## comment.json Example

```json
{
  "commentId": 45,
  "version": 1,
  "text": "Johnnie is going to take this work over.",
  "renderedText": "<p>Johnnie is going to take this work over.</p>",
  "format": "markdown",
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

Folder path for the above: `WorkItems/2026-01-15/638700501911000000-12345-c45/comment.json`
