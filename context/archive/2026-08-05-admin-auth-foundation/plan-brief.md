# Admin Authentication Foundation — Plan Brief

> Full plan: `context/changes/admin-auth-foundation/plan.md`

## What & Why

Wire cookie-based login/logout into the panel, gating a first placeholder page behind `[Authorize]`. This is roadmap `F-02` — every panel slice (S-01 through S-05) needs an authenticated session to build against, and FR-011 requires email+password login with no self-registration or password reset.

## Starting Point

`src/` is still pure minimal-API — no UI framework, no auth services, no `User`/`Admin` entity. `LassieDbContext` and its audit-log convention exist from F-01, but nothing has used them yet. This is the first slice to introduce any UI at all.

## Desired End State

An admin can open the panel, log in with a seeded email/password, land on a placeholder "panel" page showing their email, stay logged in across page reloads for up to 14 days, and log out — all working both locally and in production at `kododo.dev/lassie`. Any future page under the panel just needs `[Authorize]` to be protected the same way.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| UI framework | Blazor Server | User's pick — interactive C# UI, no separate frontend project/build to stand up. |
| Auth mechanism | ASP.NET Core cookie authentication | Standard, well-documented for server-rendered UIs; avoids full Identity's overhead for one flat admin role. |
| Password hashing | `PasswordHasher<User>` (Microsoft.Extensions.Identity.Core) | Battle-tested PBKDF2 without pulling in Identity's stores/UserManager. |
| Data model | `Users` table (not `Admins`) | User's naming choice — leaves room for the PRD's "Forward" multi-admin future without a rename. |
| Admin implements `IAuditable`? | No | A password hash landing in `AuditLog.Snapshot` would be a secrecy leak; no audit use case for one credential. |
| First-admin provisioning | Startup seed from `ADMIN_EMAIL`/`ADMIN_PASSWORD` env vars, only when `Users` is empty | Resolves the roadmap's open unknown; matches the existing `.env` → `docker-compose.yml` secrets convention. |
| Session length | 14-day sliding cookie | Single admin, no password-reset flow — short forced re-logins are pure friction with no real security upside at this risk profile. |
| Logout | In scope | Cheap to add with cookie auth (`SignOutAsync`); a login without logout is an incomplete session lifecycle. |
| Brute-force protection | None (deferred) | Single admin, private VPS, no public registration — low attack surface for a 3-week MVP. |
| Change-own-password | Out of scope | Not in FR-011; only path to change it is a manual DB/reseed step for now. |
| Test project | Still deferred | Consistent with F-01's decision; verified manually again here. |
| Login attempt logging | Yes, structured `ILogger` (success/failure, never the password) | Cheap observability for diagnosing lockouts or suspicious access, using infra that already exists. |

## Scope

**In scope:** Blazor Server scaffolding, cookie auth, `Users` table + seed, login page, logout page, `[Authorize]`-gated placeholder panel page, login attempt logging, reverse-proxy path-base correctness for `kododo.dev/lassie`.

**Out of scope:** Full ASP.NET Core Identity, self-registration, password reset, change-own-password, brute-force lockout, real panel content (licenses/modules — that's S-01+), any Caddyfile changes, an automated test project.

## Architecture / Approach

Three phases, each proving itself before the next: (1) Blazor Server renders behind the existing minimal-API host, with cookie auth services and reverse-proxy path-base handling wired but nothing gated yet — a public placeholder page is the proof; (2) a `Users` table and startup seed give the app exactly one admin account, independent of any UI; (3) login/logout pages tie it together and the placeholder becomes `[Authorize]`-gated, verified both locally and against production. The one real architectural gotcha: Blazor Server's *interactive* components can't call `HttpContext.SignInAsync`/`SignOutAsync` (the SignalR circuit runs after the response starts), so login/logout must be plain static server-rendered pages — everything else in the panel can be interactive as normal.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Blazor Server + cookie-auth infrastructure | Public placeholder page rendering through the full pipeline (Blazor + auth services + path-base middleware) | Reverse-proxy path-base plumbing is easy to get subtly wrong and only shows up in production, not local dev |
| 2. Users table + password hashing + startup seed | Exactly one admin account provisioned automatically on first boot | Seed logic must be idempotent — a bug here could double-insert or crash-loop every restart |
| 3. Login/logout pages + protected gate + production verification | Full working login/logout cycle, locally and at `kododo.dev/lassie` | Static-SSR-vs-interactive split for the auth pages is the one place a naive `@rendermode InteractiveServer` would silently break sign-in |

**Prerequisites:** F-01 (`persistence-layer-foundation`, archived) — `LassieDbContext`, migrations, and deploy auto-migration all already work.
**Estimated effort:** ~2-3 sessions across 3 phases, matching F-01's pace.

## Open Risks & Assumptions

- The reverse-proxy path-base fix (custom middleware forcing `Request.PathBase`, since Caddy's `handle_path` already strips the prefix before the app sees it) is based on documented ASP.NET Core/Blazor behavior, not on inspecting the sibling apps' source (`runway-demo-web`/`configway-demo-web` aren't in this repo) — Phase 3's production verification step is what actually confirms it works, mirroring how F-01 only proved production behavior in its final phase.
- 14-day sliding sessions and no lockout are both accepted-risk decisions for MVP scale (single admin, private VPS) — revisit if the threat model changes (e.g., multiple admins, public internet exposure of the login page becomes a real concern beyond "just this VPS").

## Success Criteria (Summary)

- Admin logs in with seeded email/password and reaches the placeholder panel page.
- Unauthenticated access to any `[Authorize]`-gated page redirects to `/login`, both locally and at `kododo.dev/lassie`.
- Session persists across reloads for the configured window; logout ends it immediately.
