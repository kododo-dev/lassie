<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Admin Authentication Foundation

- **Plan**: context/changes/admin-auth-foundation/plan.md
- **Scope**: Phase 1-3 of 3 (Phase 3's local work included; production verification 3.6/3.7 still pending)
- **Date**: 2026-08-06 (re-verified after triage fixes)
- **Verdict**: APPROVED
- **Findings**: 0 critical, 0 warnings, 2 observations (all warnings resolved since the prior pass)

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | PASS |
| Safety & Quality | PASS |
| Architecture | PASS |
| Pattern Consistency | PASS |
| Success Criteria | PASS |

## Re-verification note

This review's first pass (same date) found F1 (undocumented `Routes.razor`/`RedirectToLogin.razor` deviation) and F2 (timing side-channel in `Login.razor`), both WARNING/LOW-impact. Both were fixed and verified in the same session:

- **F1** — `plan.md`'s Phase 1 `Routes.razor` contract now explains the `AuthorizeRouteView` requirement (plain `RouteView` silently ignores `[Authorize]`); `RedirectToLogin.razor` added to Phase 3's file list.
- **F2** — `Login.razor` now verifies against a fixed dummy hash (`DummyPasswordHash`, computed once via `PasswordHasher<User>`) when no user matches the submitted email, instead of short-circuiting straight to `Failed`. Verified: `dotnet build` clean, browser regression test against an unknown email still returns the generic "Invalid email or password." with a `warn: Login failed for ...` server log — same behavior as before, timing gap closed.

This pass re-read both changed files (`plan.md`, `src/Components/Pages/Login.razor`), re-ran `dotnet build src/lassie.csproj` (0 errors), and confirmed the `## Progress` section's heading/checkbox structure is still mechanically consistent. No new findings introduced by the fixes; no other files touched since the prior pass. The two OBSERVATIONs (F3, F4 below) were explicitly SKIPPED by the user in the prior triage as accepted, out-of-scope risk — carried forward unchanged.

## Findings (history)

### F1 — Routes.razor/RedirectToLogin.razor deviation undocumented in the plan — RESOLVED

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: src/Components/Routes.razor; src/Components/RedirectToLogin.razor
- **Detail**: `Routes.razor` was upgraded from plain `<RouteView>` to `<AuthorizeRouteView>` + a new `RedirectToLogin.razor` — necessary (`RouteView` ignores `[Authorize]`) but undocumented in `plan.md`.
- **Fix applied**: `plan.md`'s Phase 1 item 2 contract now documents the `AuthorizeRouteView` requirement and rationale; `RedirectToLogin.razor` added to Phase 3 item 3's file list.
- **Decision**: FIXED

### F2 — Timing side-channel on nonexistent email in Login.razor — RESOLVED

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/Components/Pages/Login.razor
- **Detail**: The null-user branch short-circuited past `VerifyHashedPassword`, creating a measurable timing gap that leaked whether an email existed.
- **Fix applied**: Null-user branch now calls `PasswordHasher.VerifyHashedPassword` against a fixed dummy hash, equalizing cost with the real-user branch. Regression-verified in browser: unknown email still shows the generic error with no functional change.
- **Decision**: FIXED

### F3 — Dev-only secrets committed in appsettings.Development.json

- **Severity**: 📝 OBSERVATION
- **Dimension**: Safety & Quality
- **Location**: src/appsettings.Development.json:9-11
- **Detail**: `ADMIN_PASSWORD: "devpassword123"` and the local Postgres password are committed to git. Confirmed acceptable: standard ASP.NET Core convention, clearly throwaway values, real production secrets correctly gitignored via `deploy/.env` (with `.env.example` holding only a placeholder).
- **Fix**: Optional — a one-line `lessons.md` note that this file must never carry a non-local-only credential.
- **Decision**: SKIPPED

### F4 — Unsynchronized check-then-act in startup admin seed

- **Severity**: 📝 OBSERVATION
- **Dimension**: Safety & Quality
- **Location**: src/Program.cs (seed block, `if (!context.Users.Any())`)
- **Detail**: No transaction/lock around the check-then-insert. Two concurrent instances against an empty DB could race. Out of scope for the current single-instance VPS deployment.
- **Fix**: None needed now — flag for whoever adds replicas.
- **Decision**: SKIPPED

## Success Criteria Verification

**Automated** (re-run against current working tree, post-fix):
- `dotnet build src/lassie.csproj` → PASS (0 errors; 2 pre-existing `NU1510` pruning warnings + 1 `BL0008` form-binding warning, both expected/benign)
- `src/Migrations/*_AddUsers.cs` exists → PASS

**Manual** (per plan's Progress section):
- Phase 1 (1.2, 1.3): local boot + placeholder render, SignalR connects, zero console errors — checked `[x]`, evidence: browser console read + curl checks in-session — 3c96ebb
- Phase 2 (2.3, 2.4): seed creates exactly one `Users` row with hashed password, idempotent on restart — checked `[x]`, evidence: `psql` output + server logs across two boots — 68e8d51
- Phase 3 (3.2-3.5): full local login/logout/re-gate cycle — checked `[x]`, evidence: live browser walkthrough this session; antiforgery independently confirmed active via a bare curl POST returning 400; post-fix regression re-tested for the unknown-email path
- Phase 3 (3.6, 3.7): production verification — correctly still `[ ]`, pending a push to `main`; not rubber-stamped

No rubber-stamping observed — every checked manual item ties to real evidence gathered in this session.

## Scope Discipline

`git diff --name-status 3c96ebb^..HEAD` plus the uncommitted Phase 3 working tree matches the plan's file list for all planned/implied changes, including `RedirectToLogin.razor` now that F1 is documented. "What We're NOT Doing" boundaries respected: no full ASP.NET Core Identity, no self-registration, no password-reset/change-password UI, no lockout, no Caddyfile changes, no test project stood up.
