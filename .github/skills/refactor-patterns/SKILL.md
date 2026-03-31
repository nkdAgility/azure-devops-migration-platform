# Refactor Patterns — Skill Instructions

## Role

When this skill is active, assess code for quality issues and apply safe refactoring patterns. All refactoring must occur only after the full test suite is passing.

---

## Pattern 1 — Eliminate In-Memory Buffering

**Smell:** A method collects all revision paths into a `List<T>` before processing.

```csharp
// BAD — loads everything into memory
var paths = await store.EnumerateAsync("WorkItems/").ToListAsync();
foreach (var path in paths) { ... }
```

**Fix:** Stream directly from `IAsyncEnumerable<T>`:

```csharp
// GOOD — streaming
await foreach (var path in store.EnumerateAsync("WorkItems/", cancellationToken))
{
    await ProcessRevisionAsync(path, cancellationToken);
    await WriteCursorAsync(path, cancellationToken);
}
```

---

## Pattern 2 — Cursor Written at Wrong Granularity

**Smell:** Cursor is written once at the end of the entire run.

```csharp
// BAD — cursor only written on completion; crash loses all progress
await foreach (var path in store.EnumerateAsync(...))
    await ProcessRevisionAsync(path, cancellationToken);
await stateStore.WriteAsync("workitems.cursor.json", lastPath, cancellationToken);
```

**Fix:** Write the cursor after each successfully processed item:

```csharp
// GOOD
await foreach (var path in store.EnumerateAsync(...))
{
    await ProcessRevisionAsync(path, cancellationToken);
    await stateStore.WriteAsync("workitems.cursor.json", path, cancellationToken);
}
```

---

## Pattern 3 — Async Void or Blocking Calls

**Smell:** `async void` event handlers or `.Result` / `.Wait()` calls that can deadlock.

```csharp
// BAD
public async void ProcessAsync() { ... }
var result = SomeTask().Result;
```

**Fix:** Use `async Task` and `await`:

```csharp
// GOOD
public async Task ProcessAsync() { ... }
var result = await SomeTask();
```

---

## Pattern 4 — Direct Concrete Store Reference in Module

**Smell:** A module instantiates or references `FileSystemArtefactStore` directly.

```csharp
// BAD
var store = new FileSystemArtefactStore(basePath);
```

**Fix:** Inject `IArtefactStore` via constructor:

```csharp
// GOOD
public WorkItemsModule(IArtefactStore artefactStore, IStateStore stateStore) { ... }
```

---

## Pattern 5 — Inline Identity Resolution

**Smell:** A module calls an identities endpoint directly.

```csharp
// BAD
var targetUser = await _targetClient.GetUserAsync(sourceUser);
```

**Fix:** Delegate to `IIdentityMappingService`:

```csharp
// GOOD
var targetIdentity = await _identityMapping.ResolveAsync(sourceUser, cancellationToken);
```

---

## Refactoring Checklist (Post-Green)

Run through this list after the test suite is green:

- [ ] No `ToList()` or `ToArray()` call on an enumeration of revision paths.
- [ ] Cursor written inside the processing loop, not outside.
- [ ] No `async void` in production code.
- [ ] No `.Result` or `.Wait()` calls.
- [ ] No references to concrete `FileSystemArtefactStore` or `AzureBlobArtefactStore` inside modules.
- [ ] No inline identity resolution.
- [ ] All new public methods have XML doc comments.
- [ ] No TODO or FIXME comments in production paths.
