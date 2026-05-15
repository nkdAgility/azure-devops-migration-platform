# ID Mapping Database Schema

**Location**: `.migration/Checkpoints/idmap.db`

**Purpose**: SQLite database storing ID mappings for work items, attachments, and embedded images. Enables idempotent resume from checkpoints without duplicate creation.

**Format**: SQLite 3

---

## Tables

### 1. work_item_mappings

Maps source work item IDs to target work item IDs.

```sql
CREATE TABLE work_item_mappings (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    source_id INTEGER NOT NULL UNIQUE,
    target_id INTEGER NOT NULL,
    work_item_type TEXT NOT NULL,
    created_at TEXT NOT NULL,  -- ISO 8601 timestamp
    revision_count INTEGER DEFAULT 0
);
```

| Column | Type | Nullable | Description |
|--------|------|----------|-------------|
| id | INTEGER | No | Auto-incrementing primary key |
| source_id | INTEGER | No | Source system work item ID (UNIQUE) |
| target_id | INTEGER | No | Target system work item ID |
| work_item_type | TEXT | No | Work item type name (e.g., "User Story", "Bug") |
| created_at | TEXT | No | ISO 8601 timestamp when mapping created |
| revision_count | INTEGER | Yes | Count of revisions applied for this work item |

**Indexes**:
- UNIQUE (source_id) — Prevent duplicate source mappings
- INDEX (target_id) — Fast lookup by target ID for validation

**Usage**:
```sql
-- Insert mapping on first revision
INSERT INTO work_item_mappings (source_id, target_id, work_item_type, created_at)
VALUES (42, 123, 'User Story', '2026-05-13T14:32:10.123Z');

-- Increment revision count on subsequent revisions
UPDATE work_item_mappings SET revision_count = revision_count + 1
WHERE source_id = 42;

-- Lookup mapping during import
SELECT target_id FROM work_item_mappings WHERE source_id = 42;
```

---

### 2. attachment_mappings

Maps source attachment IDs to target attachment IDs and metadata.

```sql
CREATE TABLE attachment_mappings (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    source_id TEXT NOT NULL,
    target_id TEXT NOT NULL,
    source_work_item_id INTEGER NOT NULL,
    target_work_item_id INTEGER NOT NULL,
    file_name TEXT NOT NULL,
    file_size_bytes INTEGER NOT NULL,
    content_hash TEXT NOT NULL,
    created_at TEXT NOT NULL,  -- ISO 8601 timestamp
    UNIQUE (source_id, source_work_item_id)
);
```

| Column | Type | Nullable | Description |
|--------|------|----------|-------------|
| id | INTEGER | No | Auto-incrementing primary key |
| source_id | TEXT | No | Source attachment ID (from exported package) |
| target_id | TEXT | No | Target attachment ID (assigned by target system) |
| source_work_item_id | INTEGER | No | Source work item ID that owns this attachment |
| target_work_item_id | INTEGER | No | Target work item ID (mapped) |
| file_name | TEXT | No | File name from package |
| file_size_bytes | INTEGER | No | File size in bytes |
| content_hash | TEXT | No | SHA256 hash of file content |
| created_at | TEXT | No | ISO 8601 timestamp when attachment uploaded |

**Constraints**:
- UNIQUE (source_id, source_work_item_id) — Prevent duplicate uploads for same work item

**Indexes**:
- INDEX (source_work_item_id) — Fast lookup of attachments for a work item
- INDEX (target_work_item_id) — Fast lookup of target attachments
- INDEX (content_hash) — Detect duplicate binaries across work items

**Usage**:
```sql
-- Insert mapping after successful upload
INSERT INTO attachment_mappings 
(source_id, target_id, source_work_item_id, target_work_item_id, 
 file_name, file_size_bytes, content_hash, created_at)
VALUES ('att-123', 'WIURI-456', 42, 123, 'document.pdf', 15360, 
        'abc123...', '2026-05-13T14:32:10.123Z');

-- Check if attachment already uploaded (for resume)
SELECT target_id FROM attachment_mappings 
WHERE source_id = 'att-123' AND source_work_item_id = 42;

-- List all attachments for a work item
SELECT file_name, target_id FROM attachment_mappings 
WHERE source_work_item_id = 42;
```

---

### 3. embedded_image_mappings

Maps source embedded image IDs to target image URLs.

```sql
CREATE TABLE embedded_image_mappings (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    source_id TEXT NOT NULL,
    target_url TEXT NOT NULL,
    source_work_item_id INTEGER NOT NULL,
    target_work_item_id INTEGER NOT NULL,
    file_name TEXT NOT NULL,
    file_size_bytes INTEGER NOT NULL,
    content_hash TEXT NOT NULL,
    referencing_fields TEXT NOT NULL,  -- JSON array: ["Description", "RegressionDetail"]
    created_at TEXT NOT NULL,  -- ISO 8601 timestamp
    UNIQUE (source_id, source_work_item_id)
);
```

| Column | Type | Nullable | Description |
|--------|------|----------|-------------|
| id | INTEGER | No | Auto-incrementing primary key |
| source_id | TEXT | No | Source image ID (generated identifier in package) |
| target_url | TEXT | No | Target system image URL (where uploaded) |
| source_work_item_id | INTEGER | No | Source work item ID that references this image |
| target_work_item_id | INTEGER | No | Target work item ID (mapped) |
| file_name | TEXT | No | File name from package |
| file_size_bytes | INTEGER | No | File size in bytes |
| content_hash | TEXT | No | SHA256 hash of file content |
| referencing_fields | TEXT | No | JSON array of field names that reference this image |
| created_at | TEXT | No | ISO 8601 timestamp when image uploaded |

**Constraints**:
- UNIQUE (source_id, source_work_item_id) — Prevent duplicate uploads for same work item

**Indexes**:
- INDEX (source_work_item_id) — Fast lookup of images for a work item
- INDEX (target_work_item_id)
- INDEX (content_hash) — Detect duplicate binaries

**Usage**:
```sql
-- Insert mapping after successful upload
INSERT INTO embedded_image_mappings 
(source_id, target_url, source_work_item_id, target_work_item_id, 
 file_name, file_size_bytes, content_hash, referencing_fields, created_at)
VALUES ('img-789', 'https://dev.azure.com/.../_apis/wit/attachmentRendering/xyz', 42, 123,
        'diagram.png', 8192, 'def456...', '["Description", "Acceptance"]', 
        '2026-05-13T14:32:10.123Z');

-- Check if image already uploaded (for resume)
SELECT target_url FROM embedded_image_mappings 
WHERE source_id = 'img-789' AND source_work_item_id = 42;

-- Get field references for content rewriting
SELECT referencing_fields FROM embedded_image_mappings 
WHERE source_id = 'img-789' AND source_work_item_id = 42;
```

---

## Migration Operations

### Initializing the Database

```sql
-- Called once at import start (or skipped if database already exists)

CREATE TABLE IF NOT EXISTS work_item_mappings (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    source_id INTEGER NOT NULL UNIQUE,
    target_id INTEGER NOT NULL,
    work_item_type TEXT NOT NULL,
    created_at TEXT NOT NULL,
    revision_count INTEGER DEFAULT 0
);

CREATE TABLE IF NOT EXISTS attachment_mappings (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    source_id TEXT NOT NULL,
    target_id TEXT NOT NULL,
    source_work_item_id INTEGER NOT NULL,
    target_work_item_id INTEGER NOT NULL,
    file_name TEXT NOT NULL,
    file_size_bytes INTEGER NOT NULL,
    content_hash TEXT NOT NULL,
    created_at TEXT NOT NULL,
    UNIQUE (source_id, source_work_item_id)
);

CREATE TABLE IF NOT EXISTS embedded_image_mappings (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    source_id TEXT NOT NULL,
    target_url TEXT NOT NULL,
    source_work_item_id INTEGER NOT NULL,
    target_work_item_id INTEGER NOT NULL,
    file_name TEXT NOT NULL,
    file_size_bytes INTEGER NOT NULL,
    content_hash TEXT NOT NULL,
    referencing_fields TEXT NOT NULL,
    created_at TEXT NOT NULL,
    UNIQUE (source_id, source_work_item_id)
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_work_item_source ON work_item_mappings(source_id);
CREATE INDEX IF NOT EXISTS idx_work_item_target ON work_item_mappings(target_id);

CREATE INDEX IF NOT EXISTS idx_attachment_source_wi ON attachment_mappings(source_work_item_id);
CREATE INDEX IF NOT EXISTS idx_attachment_target_wi ON attachment_mappings(target_work_item_id);
CREATE INDEX IF NOT EXISTS idx_attachment_content ON attachment_mappings(content_hash);

CREATE INDEX IF NOT EXISTS idx_image_source_wi ON embedded_image_mappings(source_work_item_id);
CREATE INDEX IF NOT EXISTS idx_image_target_wi ON embedded_image_mappings(target_work_item_id);
CREATE INDEX IF NOT EXISTS idx_image_content ON embedded_image_mappings(content_hash);
```

### Detecting Resume Scope

```sql
-- Query to determine what has already been processed:

-- Get last processed work item
SELECT source_id FROM work_item_mappings ORDER BY created_at DESC LIMIT 1;

-- Get attachment IDs already uploaded
SELECT source_id FROM attachment_mappings WHERE source_work_item_id = @currentWorkItemId;

-- Get image IDs already uploaded
SELECT source_id FROM embedded_image_mappings WHERE source_work_item_id = @currentWorkItemId;
```

### Detecting Duplicates on Resume

```sql
-- Before uploading an attachment during resume:

SELECT COUNT(*) FROM attachment_mappings 
WHERE source_id = @sourceAttachmentId AND source_work_item_id = @sourceWorkItemId;

-- If count > 0, attachment already uploaded; skip upload and use stored target_id.
```

---

## Version & Evolution

**Current Schema Version**: 1.0

**Forward Compatibility**:
- Adding new columns: Use `ALTER TABLE ... ADD COLUMN` with default values (safe on resume).
- Removing columns: Not supported (would break mapping lookups).
- Renaming columns: Not supported (breaks queries).
- Schema changes require version increment and graceful upgrade logic in `ImportCheckpointService`.

**Upgrade Pattern** (if schema version changes in future):

```csharp
private async Task UpgradeIdMapDbIfNeeded(string dbPath, int currentVersion)
{
    using var connection = new SqliteConnection($"Data Source={dbPath}");
    await connection.OpenAsync();
    
    // Detect existing version
    var storedVersion = await GetSchemaVersion(connection);
    
    if (storedVersion < currentVersion)
    {
        // Run migration SQL
        await connection.ExecuteAsync("ALTER TABLE work_item_mappings ADD COLUMN new_field TEXT");
        await SetSchemaVersion(connection, currentVersion);
    }
}
```

---

## Access Patterns

### Import Module Uses

```csharp
// Check if work item already mapped
var mapping = await _idMapDb.QueryFirstOrDefaultAsync<WorkItemMapping>(
    "SELECT target_id FROM work_item_mappings WHERE source_id = @sourceId",
    new { sourceId = sourceWorkItemId });

if (mapping != null)
{
    // Reuse existing mapping
    context.TargetWorkItemId = mapping.TargetId;
}
else
{
    // Create new work item and store mapping
    var newTargetId = await _workItemService.CreateAsync(...);
    await _idMapDb.ExecuteAsync(
        "INSERT INTO work_item_mappings (source_id, target_id, work_item_type, created_at) VALUES (@sourceId, @targetId, @type, @now)",
        new { sourceId = sourceWorkItemId, targetId = newTargetId, type = "User Story", now = DateTime.UtcNow });
}
```

### Validation Module Uses

```csharp
// Verify all work items mapped (for validation report)
var unmappedCount = await _idMapDb.ExecuteScalarAsync<int>(
    "SELECT COUNT(*) FROM work_item_mappings WHERE target_id IS NULL");

// Detect duplicate attachments
var duplicateAttachments = await _idMapDb.QueryAsync(
    "SELECT content_hash, COUNT(*) as cnt FROM attachment_mappings GROUP BY content_hash HAVING cnt > 1");
```

---

## Backup & Recovery

### Backup Before Import

```bash
# Copy idmap.db before starting import
cp .migration/Checkpoints/idmap.db .migration/Checkpoints/idmap.db.backup
```

### Recovery After Failure

```bash
# If import is corrupted, restore from backup and checkpoint rollback is handled by import module
cp .migration/Checkpoints/idmap.db.backup .migration/Checkpoints/idmap.db

# Re-run import; it will use checkpoint to resume from last complete stage
```

---

## Example Queries

### Query: All work items and their revision count

```sql
SELECT source_id, target_id, work_item_type, revision_count, created_at
FROM work_item_mappings
ORDER BY created_at;
```

### Query: Attachment coverage (attachments per work item)

```sql
SELECT 
    w.source_id,
    w.target_id,
    COUNT(a.id) as attachment_count
FROM work_item_mappings w
LEFT JOIN attachment_mappings a ON w.source_id = a.source_work_item_id
GROUP BY w.source_id
ORDER BY attachment_count DESC;
```

### Query: Image rewrite targets

```sql
SELECT source_id, target_url, referencing_fields, created_at
FROM embedded_image_mappings
WHERE source_work_item_id = @workItemId
ORDER BY created_at;
```

---

## Performance Considerations

- **Index strategy**: Unique index on source_id (fast lookup on resume); separate indexes on work_item_id and content_hash (lookup by work item, duplicate detection).
- **Transaction size**: Batch inserts in transactions (e.g., 100 attachments per transaction) for performance.
- **Database size**: Typical scale: 100k work items, 500k attachments, 50k images → ~200 MB SQLite database.
- **Query optimization**: All lookups use indexed columns; no table scans.

