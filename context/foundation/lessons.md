# Lessons Learned

> Append-only register of recurring rules and patterns. Re-read at start by /10x-frame, /10x-research, /10x-plan, /10x-plan-review, /10x-implement, /10x-impl-review.

## Audit snapshots require load-before-mutate

**Context**: src/Data/LassieDbContext.cs:40 (AddAuditLogEntries, SaveChanges override)

**Problem**: entry.OriginalValues only reflects true pre-change state when EF materialized the entity via a query (or was Attach()ed with original values explicitly set). An "attach-and-mark-modified" shortcut (e.g. context.Attach(new License{Id=x,...}); Entry(x).State = Modified;, or Remove(new License{Id=x}) without loading first) makes OriginalValues equal the new/default values instead of the real prior row — silently corrupting the AuditLog.Snapshot "before" record.

**Rule**: Any IAuditable entity must be loaded via a query (not attached-and-marked-dirty) before being saved as Modified or Deleted, so OriginalValues reflects real prior state.

**Applies to**: Any write path (services, minimal-API handlers) that mutates an IAuditable entity, starting with License in S-03.

## ASP.NET Core Data Protection keys aren't persisted across container restarts

**Context**: src/Program.cs (cookie authentication + antiforgery setup, admin-auth-foundation F-02); observed on the VPS deploy at `kododo.dev/lassie`.

**Problem**: ASP.NET Core's Data Protection API (used to encrypt/sign the auth cookie and antiforgery tokens) defaults to storing its key ring on local disk inside the container (`/home/app/.aspnet/DataProtection-Keys`), which isn't mounted to a persistent volume. Every `docker compose up -d` that recreates the container generates a fresh key ring, so any cookie or antiforgery token issued by the previous instance becomes undecryptable — observed live as `AntiforgeryValidationException: The antiforgery token could not be decrypted` mid-session during a redeploy. Every redeploy silently logs out all active sessions and invalidates in-flight form submissions.

**Rule**: Before any deploy topology change (more traffic, more frequent deploys, multiple admins), persist Data Protection keys outside the container — e.g. `AddDataProtection().PersistKeysToFileSystem(...)` pointed at a mounted volume, or `PersistKeysToDbContext<LassieDbContext>()` since EF Core is already wired up. Not fixed yet — accepted as low-impact for now (single admin, infrequent deploys, worst case is a re-login).

**Applies to**: Any future work touching deploy frequency, session/token lifetime guarantees, or scaling to multiple app replicas.
