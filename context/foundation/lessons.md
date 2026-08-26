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

## MudBlazor providers must live in the same render scope as per-page @rendermode consumers

**Context**: src/Components/Shared/MudProviders.razor, src/Components/ThemeState.cs (admin-panel-ui-refresh, Phase 5, commit e0cf71b)

**Problem**: A single `<MudThemeProvider>`/`<MudPopoverProvider>` pair placed once in `MainLayout.razor` works fine when every page shares one global interactive render mode, but fails at runtime ("Missing <MudPopoverProvider />") when pages use per-page `@rendermode` instead — which this app requires because `Login.razor` must stay static SSR (`HttpContext.SignInAsync` needs direct response access unavailable inside an interactive circuit). MudBlazor's providers must render in the same render-mode boundary as the components that consume them (e.g. `MudDatePicker`'s calendar popover), and a shared `MainLayout` instance doesn't satisfy that under per-page rendering.

**Rule**: (TBD — fill in the actionable rule)

**Applies to**: (TBD — fill in which future work this constrains)

## Blazor Server's per-circuit DbContext accumulates tracked entities unless explicitly detached

**Context**: src/Components/Pages/CreateLicense.razor:68-85 (license-creation-and-verification F2), src/Components/Pages/EditLicense.razor:86 (license-edit-with-audit-history F3), src/Components/Pages/PanelHome.razor:60 (license-list-view F1) — surfaced independently in three separate impl-reviews before being named as a pattern.

**Problem**: Blazor Server injects one `DbContext` per circuit (long-lived, not per-request/per-scope like a typical web request). Any query or save that doesn't explicitly detach the entity afterward — or use `.AsNoTracking()` for read-only queries — leaves it attached to the change tracker for the rest of the circuit's lifetime. This was found three times independently: `CreateLicense.razor` detached only on the failure path, not on success; `EditLicense.razor` never detached after save (fixed by detaching the *previous* entity on next load instead, since the page must stay editable across repeat saves); `PanelHome.razor`'s read-only list query had no `.AsNoTracking()` at all.

**Rule**: Any `DbContext` read or write inside a Blazor Server per-circuit-scoped component must either (a) use `.AsNoTracking()` for read-only queries, or (b) explicitly detach entities once no longer needed (post-save, or right before loading a different entity in the same page instance). Don't rely on the circuit ending soon — sessions can be long-lived.

**Applies to**: Any future Blazor Server page/component that queries or mutates entities via the circuit-scoped `DbContext`.

## Shared integration-test fixtures need null-guarded teardown and must document commits outside the per-test rollback

**Context**: src/Lassie.Tests/Infrastructure/IntegrationTestBase.cs:17-46 (testing-backend-critical-path-coverage F1, F2) — two related gaps found in the same review, not (yet) recurring across separate changes like the DbContext lesson above, but worth naming before more tests build on this fixture.

**Problem**: `InitializeAsync` assigns `_connection`, `Factory`, `HttpClient`, `_transaction`, and `DbContext` sequentially across several fallible `await`s. If any step throws before the last assignment, later fields stay `null!`, and `DisposeAsync` dereferenced them unconditionally — so a partial `InitializeAsync` failure surfaced as a masking `NullReferenceException` instead of the real error, and skipped disposing whatever *was* acquired. Separately, `Factory.CreateClient()` runs `Migrate()` + an admin-user seed *before* the per-test transaction begins (intentional, to avoid nesting migration transactions) — that seed insert is a real, non-rolled-back commit against the collection-shared Postgres container, so the seeded admin `User` row persists across every test in the run as an undocumented order-dependent side effect.

**Rule**: Any shared test fixture that acquires multiple fallible resources sequentially must null-guard every field in its async teardown, regardless of how far setup got. When setup intentionally commits data outside the per-test rollback boundary (e.g., migrations/seed data that can't be transaction-nested), document that specific side effect where the fixture is defined, so a later test asserting on that table doesn't hit a confusing pre-existing row.

**Applies to**: `IntegrationTestBase` and any future shared test fixture using the Testcontainers/shared-collection + per-test-transaction rollback pattern.
