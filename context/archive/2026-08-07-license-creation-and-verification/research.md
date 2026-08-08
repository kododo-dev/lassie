---
date: 2026-08-07T20:05:46+02:00
researcher: Claude (Sonnet 5)
git_commit: b6123a2cf02bde9afa61cf1cf207c3861ce6d476
branch: main
repository: lassie
topic: "License creation + verification API: entity design, API-key secrecy, and M2M endpoint auth for S-02"
tags: [research, codebase, license-entity, api-key, verification-api, prd]
status: complete
last_updated: 2026-08-07
last_updated_by: Claude (Sonnet 5)
---

# Research: License creation and client-app verification (S-02)

**Date**: 2026-08-07T20:05:46+02:00
**Researcher**: Claude (Sonnet 5)
**Git Commit**: b6123a2cf02bde9afa61cf1cf207c3861ce6d476
**Branch**: main
**Repository**: lassie

## Research Question

Roadmap slice `S-02` (north star, unblocked — `F-01`/`F-02` both `done`): admin creates a license (text label + optional expiry date), the system generates a unique API key, and a client app verifies the license's validity through a machine-to-machine (M2M) API using that key.

1. What entity-modeling conventions does this codebase already establish (`User`, `AuditLog`, migrations) that a new `License` entity should follow?
2. How should the API key be generated, hashed, stored, and revealed exactly once — given the PRD's hard requirement that it's never shown in plaintext again after creation (not in the panel, not in logs)?
3. How does the existing HTTP pipeline (cookie-only auth, minimal-API sample, Blazor Server routing) need to change to host a second, API-key-authenticated M2M endpoint alongside the cookie-authenticated panel?
4. What prior decisions, NFRs, and reusable fixes (from PRD, roadmap, lessons, and the reverted `module-catalog-management` plan-review) constrain or inform this slice?

## Summary

The codebase has zero existing code for `License`, API keys, or a second auth scheme — this is fully greenfield, but the conventions to follow are unambiguous and consistent across `User`/`AuditLog`/migrations. The main non-obvious design fork this research surfaces (not decided here — flagged for `/10x-plan`) is **how the API key is looked up on verification**: `PasswordHasher<T>` (the only hashing tool in the codebase) embeds a random salt per hash, so it cannot support "hash the incoming key and query by equality" — that only works for a deterministic digest (e.g. SHA-256) on a value with enough entropy that dictionary/rainbow-table attacks aren't the threat model. A second fork concerns audit-logging: `AddAuditLogEntries` snapshots an `IAuditable` entity's *entire* `OriginalValues` into `AuditLog.Snapshot` (jsonb) on every Modified/Deleted save, with no field-level exclusion mechanism — so if `License` carries the API-key hash directly and implements `IAuditable` (matching `IAuditable.cs`'s own doc-comment example, which already names `License`), the hash would leak into audit history the first time a license is edited (S-03) or deactivated (S-04). `User` sidesteps this exact problem today by simply not implementing `IAuditable` at all, specifically because of `PasswordHash` (see Historical Context). The clean, evidence-grounded fix is to keep the API-key material in a sibling entity that never implements `IAuditable`, while `License` itself (label, expiry, later status) does.

Beyond that, everything else is a straightforward following-precedent job: `long` identity PKs, `required string` properties, unique indexes via `HasIndex(...).IsUnique()`, EF's default int-enum mapping with no `HasConversion`, minimal-API endpoint registered in `Program.cs` next to `/weatherforecast`, and a new, explicitly-targeted authentication scheme so the M2M endpoint doesn't inherit the cookie scheme's redirect-to-`/login` behavior on a missing/bad key.

## Detailed Findings

### Entity & persistence conventions (`User`, `AuditLog`, migrations)

- `src/Data/LassieDbContext.cs:9-10` — `DbSet<T>` registered as an expression-bodied property: `public DbSet<AuditLog> AuditLogs => Set<AuditLog>();`.
- `src/Data/LassieDbContext.cs:12-26` (`OnModelCreating`) — always calls `base.OnModelCreating(...)` first, then per-entity fluent config:
  - Unique single-column index: `.HasIndex(u => u.Email).IsUnique()` — the pattern to reuse for the API-key lookup column.
  - Non-unique composite index: `.HasIndex(a => new { a.EntityName, a.EntityId })` — no `.IsUnique()` call.
  - No `HasConversion` calls anywhere in the codebase (confirmed via grep) — enum properties (e.g. `AuditChangeType` on `AuditLog`) map to `integer` columns via EF's implicit default. A `License` status enum, if added, would follow the same zero-config pattern.
- `src/Data/Users/User.cs` (full file):
  ```csharp
  public class User
  {
      public long Id { get; set; }
      public required string Email { get; set; }
      public required string PasswordHash { get; set; }
  }
  ```
  Plain class, `long Id`, `required string` for non-nullable reference properties, no XML doc comments, no interfaces. **`User` does not implement `IAuditable`.**
- `src/Data/Auditing/AuditLog.cs` (full file) — enum + entity co-located in one file; `DateTimeOffset` properties named `<Verb>AtUtc` (e.g. `ChangedAtUtc`).
- `src/Data/Auditing/IAuditable.cs` (full file) — empty marker interface. Its own XML doc-comment example already names `License` explicitly: `Attach(new License { Id = x }); Entry(x).State = Modified;` — this file was written anticipating `License` as an `IAuditable` implementer.
- `src/Data/LassieDbContext.cs:42-61` (`AddAuditLogEntries`) — filters `ChangeTracker.Entries()` to `e.Entity is IAuditable && e.State is EntityState.Modified or EntityState.Deleted` (**`Added` is never audited**), then does `System.Text.Json.JsonSerializer.Serialize(entry.OriginalValues.ToObject())` — the **entire entity**, not selected fields — into `AuditLog.Snapshot`.
- Migrations (`src/Migrations/`, 5 migrations + snapshot): `bigint` identity-by-default PKs, `string` → `text` (unbounded, never `varchar(n)`), `DateTimeOffset` → `timestamp with time zone`, enum → `integer`, FK/cascade via `onDelete: ReferentialAction.Cascade` inferred from an `<Entity>Id`-shaped property, naming `FK_<Dependent>_<Principal>_<Col>` / `IX_<Table>_<Col1>_<Col2>`. `20260807174444_RemoveLicenseFields.cs` (the just-added revert migration) is the most recent and cleanest template for migration style.
- No `DataType`/`ApiKey`/`License*` stub exists anywhere in `src/` — confirmed via repo-wide grep. Fully greenfield.

### API-key generation, hashing, and reveal-once

- `src/Program.cs:40` — `builder.Services.AddSingleton<PasswordHasher<User>>();`, justified by the comment at `:38-39`: safe as Singleton because its only state is immutable config + a thread-safe `RandomNumberGenerator`. This is the DI-lifetime precedent for any new hasher-shaped service.
- `src/Program.cs:71-95` — the only place currently hashing a secret before persisting it: `passwordHasher.HashPassword(admin, adminPassword)` during the idempotent first-boot admin seed.
- **No existing code generates a cryptographically random secret anywhere in `src/`** (confirmed via grep for `RandomNumberGenerator`, `Guid.NewGuid`, `Convert.ToBase64String/ToHexString`, `SHA256`, `HMACSHA256` — zero hits outside the one explanatory comment). Whatever generates the API key will be the first such component in this codebase; it needs `System.Security.Cryptography.RandomNumberGenerator` directly (BCL, no new package needed).
- `src/Components/Pages/Login.razor:46-47` — a fixed `DummyPasswordHash` is verified against on a "user not found" miss, so both branches of the login check pay equal PBKDF2 cost (timing-safe lookup pattern). Worth carrying forward for the API-key verification path if it also branches on found/not-found.
- **`Microsoft.Extensions.Identity.Core` 10.0.10** (`src/lassie.csproj:16`) is already referenced but only for `PasswordHasher<TUser>`/`PasswordVerificationResult` — no `AddIdentityCore()`, no `UserManager`, no store implementation exists (deliberately, per the archived admin-auth plan: *"one flat admin account doesn't need it"*). Reflecting over the referenced assembly confirms it has nothing purpose-built for a freestanding high-entropy API key — every token-shaped type (`TokenOptions`, `*TokenProvider<T>`) is for TOTP/email/phone confirmation codes tied to `UserManager`, which this app doesn't use.
- `deploy/.env.example:15-19` / `deploy/docker-compose.yml:6-12` — the only existing secrets are externally-supplied (`ADMIN_EMAIL`/`ADMIN_PASSWORD`, DB credentials) via `.env` → compose `environment:` → `IConfiguration`. `context/foundation/infrastructure.md:74` already anticipates "license-key signing material" as a secret category living in the VPS `.env`, though that line was written before this slice was scoped — worth confirming during planning whether it refers to something this slice actually needs (it doesn't appear to: the key is generated and hashed per-license at runtime, not a shared signing secret).

**Architecture insight — the lookup problem** (not resolved by any existing code; flagged for `/10x-plan`): `PasswordHasher<T>.HashPassword` embeds a random salt in its output, so hashing the same input twice produces different strings — this is why `Login.razor` can only *verify* a password against a specific, already-identified `User` row (found via the plaintext, indexed `Email`). An incoming API key has no such stable plaintext lookup column (storing the key itself in plaintext would violate the PRD's non-negotiable NFR). Two evidence-grounded options:
1. **Deterministic digest for lookup** (e.g. SHA-256 of the raw key) stored in an indexed, unique column — verification becomes a single indexed `WHERE KeyHash = <computed>` query. Safe *because* the key itself is high-entropy/randomly generated (unlike a human password), so precomputed dictionary/rainbow-table attacks aren't the threat model — a fast deterministic hash is standard practice for API keys of sufficient entropy (this is a general security fact, not something found in this codebase — flagged as an assumption for `/10x-plan` to confirm, since the codebase itself has no precedent either way).
2. **Split key** (`<publicId>.<secret>`, mirroring how many API-key schemes work elsewhere): store `publicId` in plaintext/indexed for O(1) lookup, then verify `secret` against a `PasswordHasher`-style salted hash of just that part. More moving parts, no existing precedent in this codebase to justify the extra complexity given FR-008's Non-Goal of key rotation.

**Architecture insight — audit-log leakage risk**: `AddAuditLogEntries` (see above) serializes the entity's *entire* `OriginalValues` into `AuditLog.Snapshot` on every Modified/Deleted save, with no field-level exclusion. `context/archive/2026-08-05-admin-auth-foundation/plan.md:118` records why `User` deliberately does **not** implement `IAuditable`: *"a password hash is exactly the kind of value that must never land in a readable AuditLog.Snapshot."* The exact same reasoning applies to an API-key hash. Since `IAuditable.cs`'s own example already anticipates `License` implementing the interface (and `lessons.md:13` ties the load-before-mutate rule to *"License in S-03"*, i.e. `License` is expected to become `IAuditable` when edits ship), the key material should not live on `License` itself if `License` is going to be audited. Keeping the API-key hash (and whatever lookup column option 1/2 above needs) on a separate, never-`IAuditable` sibling entity — e.g. one row per license, created once, never edited (consistent with FR-008's Non-Goal: no rotation without a new license) — sidesteps the leak entirely without needing new snapshot-exclusion logic that doesn't exist today.

### HTTP pipeline / routing for the M2M endpoint

- `src/Program.cs:23-34` — `AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(...)` registers **Cookie as both the only scheme and the default scheme**. `builder.Services.AddAuthorization()` (`:36`) has no policies configured; `app.UseAuthorization()` (`:107`) is wired but nothing in `Program.cs` currently calls `.RequireAuthorization()` or `[AllowAnonymous]` on any endpoint — the Blazor auth gate lives entirely at the component level (`Routes.razor`'s `<AuthorizeRouteView>` + `NotAuthorized` → `<RedirectToLogin />`), not via endpoint metadata.
- **Consequence**: a new M2M endpoint that calls `.RequireAuthorization()` without specifying an explicit scheme/policy would be evaluated against the *default* (Cookie) scheme and redirect an unauthenticated machine client to `/login` (per `options.LoginPath`) instead of returning a clean 401/JSON body. A second auth scheme must be registered and the endpoint must explicitly target it (e.g. `.RequireAuthorization(policy => policy.AddAuthenticationSchemes("ApiKey"))`), or the endpoint can skip ASP.NET Core's authentication middleware entirely and validate the key manually inside the handler (simpler, no new scheme registration, still returns whatever status code the handler chooses) — both are viable; no existing precedent favors one over the other since this is the first non-cookie-authenticated endpoint in the app.
- `src/Program.cs:110-130` — routing precedent: `MapRazorComponents<App>().AddInteractiveServerRenderMode()` is registered first, then `app.MapGet("/weatherforecast", ...).WithName("GetWeatherForecast")` — plain minimal API, no auth, no `.WithOpenApi()`. Razor Component routes and minimal-API routes are independent endpoint sources against the same `IEndpointRouteBuilder`; a new `/api/...`-style route won't collide with or get caught by the Blazor router/`RedirectToLogin` gate (confirmed via full read of `Routes.razor`, `RedirectToLogin.razor`, `App.razor`, `_Imports.razor` — none of them touch non-Blazor endpoints).
- `src/Program.cs:44-69` — `ForwardedHeaders` (`XForwardedProto|XForwardedFor`, all trust-network checks cleared since Kestrel is only ever reached via Caddy) and the manual `PathBase`-from-`ASPNETCORE_PATHBASE` middleware both run unconditionally on every request, before routing — so a client app hitting `https://kododo.dev/lassie/api/verify` is corrected the same way panel requests are. Since a stateless JSON API endpoint won't be generating self-referencing URLs, `PathBase` correctness matters far less here than it did for panel redirects, but Caddy's `handle_path /lassie*` prefix-stripping (confirmed in `deploy-plan.md:34-36`) means the route itself is still reached correctly without extra server-side path handling.
- `src/lassie.csproj` — no auth-handler package (e.g. `AspNetCore.Authentication.ApiKey`), no rate-limiting package referenced, no MVC/controllers package (the `Microsoft.NET.Sdk.Web` SDK supports minimal APIs natively, matching the existing `/weatherforecast` precedent). `Microsoft.AspNetCore.OpenApi`/`Microsoft.OpenApi` are present and wired via `AddOpenApi()`/`MapOpenApi()`, dev-only (`Program.cs:13,100`).
- `src/lassie.http` — still just the scaffolded `/weatherforecast` example; no established request/response JSON shape or auth-header convention to reuse.
- `context/foundation/tech-stack.md:24-31` — explicitly names the M2M verification API as one of the two access surfaces this stack was chosen for, citing "explicit contracts an agent can reason from... given the PRD's audit-history and key-secrecy requirements," but records no minimal-API-vs-controllers or auth-scheme decision beyond that rationale.

### Historical context, NFRs, and reusable fixes

- **PRD** (`context/foundation/prd.md`):
  - FR-005 (`:74`): create with text label + optional expiry only — no field values (FR-004 deferred, see prior change).
  - FR-006/FR-007 (`:76-79`): edit-with-audit-history and deactivate/reactivate are explicitly **S-03/S-04, not this slice**.
  - FR-008 (`:80-81`): unique API key, generation only — **no rotation/regeneration in MVP** (Non-Goal).
  - FR-009/FR-010 (`:84-88`): client app authenticates with the key, API returns validity only (no detailed invalid-reason breakdown — expired vs. deactivated is explicitly out of scope for the response shape).
  - US-01 Acceptance Criteria (`:54-58`): key unique system-wide; invalid/missing key → authorization error; response includes validity; **<500ms**.
  - NFRs (`:96-102`): key never plaintext again after generation (panel or logs); API must distinguish service-unavailable from license-invalid; <500ms.
  - Business Logic (`:104-110`): validity = current state (active/deactivated + expiry) — but deactivation is S-04, so **for this slice validity reduces to "not expired."**
- **Roadmap** (`context/foundation/roadmap.md`) S-02 entry (`:114-124`): Outcome, prerequisites (`F-01`, `F-02`, both done), and an explicit Risk callout that this slice's NFRs "deserve more scrutiny here than anywhere else on the roadmap" per `main_goal: quality`. S-03/S-04/S-05 confirmed downstream, not to be built now.
- **Lessons** (`context/foundation/lessons.md`):
  - "Audit snapshots require load-before-mutate" (`:5-13`) — explicitly scoped to *"License in S-03"*, i.e. this rule doesn't bind S-02 (creation-only, `Added` state is never audited) but does confirm `License` is expected to become `IAuditable` later, which feeds directly into the audit-leakage insight above.
  - "Data Protection keys aren't persisted across container restarts" (`:15-23`) — a redeploy mid-session still silently logs out the admin; not specific to this slice but worth remembering since this slice ships via the same deploy pipeline.
- **Reusable fixes from `context/changes/module-catalog-management/plan.md`** (plan-review findings that recurred across that change's iterations, quoted verbatim so they aren't rediscovered the hard way again):
  1. **Namespace import gap**: any new folder under `Components/` needs an explicit `@using` in `_Imports.razor`, or `@layout`/component references fail to compile.
  2. **Absolute vs. relative links**: `<a href="/logout">` (leading slash) breaks the `/lassie` path-base in production — must be `<a href="logout">` (relative). Same rule applies to any `NavigationManager.NavigateTo` call. (Already fixed at `PanelHome.razor:10`; the underlying gotcha applies to any new page this slice adds.)
  3. **Narrow `DbUpdateException` handling**: catch narrowed to `dbEx.InnerException is Npgsql.PostgresException { SqlState: "23505" }` for unique-violation-specific messaging; anything else shows a generic error so a transient DB failure isn't mislabeled as a duplicate-value problem. Directly applicable to the unique API-key-hash/unique-license-label (if labels are made unique) constraint.
  4. **`@key` on Blazor `@foreach` rows**: needed for diffing correctness on any list UI this slice adds (e.g. a license list, though the full list view is S-05 — a minimal post-creation confirmation view for this slice should still follow this if it renders a collection).
- **Infrastructure risk register** (`context/foundation/infrastructure.md:51,88`): a single-VPS deploy has brief downtime on `docker compose up -d`, and if Caddy/ACME TLS renewal fails silently, the verification API "could serve TLS errors indistinguishable from an outage, with nothing watching for it unless monitoring is built by hand" — both explicitly flagged as colliding with the "service unavailable ≠ invalid" NFR. Not something this slice's application code can fully solve, but reinforces that the verification endpoint's own error handling must never *convert* an unexpected exception into a "license invalid" response — only a deliberate, successful lookup should ever produce that result; anything else should surface as a 5xx.
- **Health-check precedent** (`context/deployment/deploy-plan.md:76-82`): the existing deploy pipeline already polls `https://kododo.dev/lassie/weatherforecast` post-deploy as a liveness check — no domain-specific health endpoint exists yet; worth considering whether the new verification endpoint (or a dedicated `/health`) should replace `/weatherforecast` in that check once it exists.

## Code References

- `src/Data/LassieDbContext.cs:9-26` — DbSet registration + `OnModelCreating` conventions (unique index, no `HasConversion`).
- `src/Data/LassieDbContext.cs:42-61` — `AddAuditLogEntries`: whole-entity snapshot, `Added` excluded, `IAuditable`-gated.
- `src/Data/Users/User.cs` — entity shape precedent; deliberately not `IAuditable`.
- `src/Data/Auditing/IAuditable.cs` — marker interface, doc comment already names `License` as the anticipated implementer.
- `src/Program.cs:23-40` — auth scheme registration (cookie-only, default), `PasswordHasher<User>` DI singleton + rationale.
- `src/Program.cs:71-95` — startup migrate + idempotent admin seed + password hashing, the only existing secret-hashing code.
- `src/Program.cs:97-130` — HTTP pipeline order, existing minimal-API route precedent (`/weatherforecast`), no per-endpoint auth requirement set anywhere yet.
- `src/Components/Pages/Login.razor:46-47` — timing-safe dummy-hash pattern on lookup miss.
- `src/Components/Pages/PanelHome.razor:10` — relative-href convention (post-fix).
- `src/Migrations/20260807174444_RemoveLicenseFields.cs` — most recent migration, cleanest style template.

## Architecture Insights

1. **API-key lookup requires a deterministic component.** `PasswordHasher<T>` (salted PBKDF2) can verify-against-a-known-row but cannot support "find the row from the presented secret alone." Plan must pick one of: (a) deterministic digest (e.g. SHA-256) as an indexed, unique lookup column — simplest, relies on the key's own entropy for safety; or (b) a split `publicId`/`secret` key. No codebase precedent favors either; this is a genuine open design decision for `/10x-plan`, not a convention to follow.
2. **Keep API-key material off any `IAuditable` entity.** `AddAuditLogEntries` snapshots whole entities with no field exclusion. `User` already avoids `IAuditable` specifically because of `PasswordHash`. `License` is expected to become `IAuditable` in S-03 (per `lessons.md` and `IAuditable.cs`'s own example). Recommendation: model the API-key hash (and its lookup column) on a separate, never-audited entity tied 1:1 to `License`, so `License` can safely implement `IAuditable` from S-02 onward (implementing it now costs nothing, since creation isn't audited, and removes a schema change later in S-03) without ever risking a key-hash leak into `AuditLog.Snapshot`.
3. **A new auth scheme, explicitly targeted, is required for the M2M endpoint** — Cookie is both the only and the default scheme today; an unscoped `.RequireAuthorization()` on the verification endpoint would misbehave (redirect-to-login) for machine clients. Manual in-handler key validation (no ASP.NET Core auth scheme at all) is the simpler alternative with no existing precedent ruling it out.
4. **"Service unavailable ≠ invalid" is primarily a discipline constraint on the endpoint's exception handling**, not a feature to build: only a deliberate, successful license lookup should ever produce a `false`/expired response; any unexpected failure (DB down, unhandled exception) must surface as a 5xx, never be coerced into "invalid." Infra-level risks (deploy downtime, silent TLS failure) are called out in `infrastructure.md` as colliding with this NFR but are out of this slice's application-code scope.

## Historical Context (from prior changes)

- `context/changes/module-catalog-management/change.md` — full account of the just-reverted configurable-field-schema slice: implemented (commits `a470efc`, `cd67a7e`, `7b5a680`), then backed out 2026-08-07 via a forward `RemoveLicenseFields` migration (not a history rewrite, since `AddLicenseFields` was already live in production) because it added scope (dynamic form rendering, dynamic value storage) not needed to prove the core hypothesis. The panel shell (`MainLayout`, Pico.css, the `PanelHome.razor` logout-link fix) from that change was kept — it's the shared layout this slice's new pages will render inside.
- `context/changes/module-catalog-management/plan.md` — source of the four reusable plan-review fixes quoted above (namespace imports, relative links, narrow `DbUpdateException`, `@key` on rows) — apply directly to this slice's panel-side CRUD/forms work.
- `context/archive/2026-08-05-admin-auth-foundation/plan.md:118` — the explicit rationale for `User` not implementing `IAuditable` ("a password hash is exactly the kind of value that must never land in a readable AuditLog.Snapshot"), the direct precedent behind Architecture Insight #2 above.
- `context/archive/2026-08-04-persistence-layer-foundation/plan.md` — established the `dotnet ef migrations add` → commit → auto-apply-on-startup (`context.Database.Migrate()`) workflow, confirmed still current in `Program.cs:71-95`.

## Related Research

- `context/changes/module-catalog-management/research.md` — prior research into license-field storage architecture (relational, not JSONB); superseded/parked for this slice's scope but relevant again whenever FR-004 is revisited post-MVP.

## Open Questions

1. **API-key lookup strategy** (deterministic digest vs. split key) — needs a decision in `/10x-plan`; see Architecture Insight #1. No security-external research was done here (this document is internal-codebase-only per the project's research/plan split) — if the plan wants to ground this decision in current best practice rather than first-principles reasoning, that's a good candidate for a short piece of *external* research (exa.ai/Context7) before or during `/10x-plan`, per `CLAUDE.md`'s internal/external research split.
2. **Where exactly does the API-key hash live** — a sibling entity (recommended, Architecture Insight #2) vs. accepting the audit-log leak risk and living with it on `License` directly. Needs a plan-level decision, not just a research note.
3. **New auth scheme vs. manual in-handler validation** for the M2M endpoint — both are viable given the current pipeline; no codebase precedent forces either.
4. **Does `License` need any status/active concept in S-02 at all**, or does validity reduce purely to "not expired" until S-04 adds deactivation? PRD's Business Logic section describes the combined rule, but S-04 owns the deactivate/reactivate mechanism — plan should decide whether to add a currently-unused `IsActive` column now (schema stability) or defer it to S-04 (avoid building ahead of need, consistent with this project's recent course-correction away from over-scoping — see `context/changes/module-catalog-management/change.md`).
