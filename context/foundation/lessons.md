# Lessons Learned

> Append-only register of recurring rules and patterns. Re-read at start by /10x-frame, /10x-research, /10x-plan, /10x-plan-review, /10x-implement, /10x-impl-review.

## Audit snapshots require load-before-mutate

**Context**: src/Data/LassieDbContext.cs:40 (AddAuditLogEntries, SaveChanges override)

**Problem**: entry.OriginalValues only reflects true pre-change state when EF materialized the entity via a query (or was Attach()ed with original values explicitly set). An "attach-and-mark-modified" shortcut (e.g. context.Attach(new License{Id=x,...}); Entry(x).State = Modified;, or Remove(new License{Id=x}) without loading first) makes OriginalValues equal the new/default values instead of the real prior row — silently corrupting the AuditLog.Snapshot "before" record.

**Rule**: Any IAuditable entity must be loaded via a query (not attached-and-marked-dirty) before being saved as Modified or Deleted, so OriginalValues reflects real prior state.

**Applies to**: Any write path (services, minimal-API handlers) that mutates an IAuditable entity, starting with License in S-03.
