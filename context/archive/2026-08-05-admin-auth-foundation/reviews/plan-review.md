<!-- PLAN-REVIEW-REPORT -->
# Plan Review: Admin Authentication Foundation

- **Plan**: context/changes/admin-auth-foundation/plan.md
- **Mode**: Deep
- **Date**: 2026-08-05
- **Verdict**: SOUND
- **Findings**: 0 critical, 1 warning, 1 observation

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| End-State Alignment | PASS |
| Lean Execution | PASS |
| Architectural Fitness | PASS |
| Blind Spots | WARNING |
| Plan Completeness | PASS |

## Grounding

8/8 paths ✓, symbols ✓ (`AddOpenApi`, `Database.Migrate`), brief↔plan ✓. No `docs/reference/contract-surfaces.md` in the project — skipped. This is the second review round on this plan; the three findings from the first round (wrong `AddInteractiveServerRenderMode()` API name, ambiguous antiforgery handling on the login form, missing `PasswordHasher<User>` DI registration) were all confirmed fixed in the current `plan.md` text.

## Findings

### F1 — VerifyHashedPassword's SuccessRehashNeeded case is silently absorbed

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Blind Spots
- **Location**: Phase 3, item 1 (Login page)
- **Detail**: `PasswordHasher<TUser>.VerifyHashedPassword` returns a `PasswordVerificationResult` enum (`Success` / `Failed` / `SuccessRehashNeeded`), not a bool — confirmed against source and current docs. The contract's "verify via VerifyHashedPassword; on success... on failure..." phrasing doesn't name the enum, so an implementer could reasonably treat any non-`Failed` result as success without ever re-hashing on `SuccessRehashNeeded`. Low real-world impact — this MVP never changes `PasswordHasherOptions` after the account is seeded, so the rehash path would never trigger — but worth naming explicitly so it's a documented simplification, not an oversight.
- **Fix**: In Phase 3 item 1's contract, name the return type explicitly: treat both `Success` and `SuccessRehashNeeded` as a valid login (no rehash-on-login logic for this slice — single seeded account, hashing options never change).
- **Decision**: ACCEPTED

### F2 — PasswordHasher<User> registered as Singleton, not Identity's idiomatic Scoped

- **Severity**: 📝 OBSERVATION
- **Dimension**: Architectural Fitness
- **Location**: Phase 2, item 5 (Startup seed)
- **Detail**: Confirmed safe: `PasswordHasher<TUser>`'s only state is immutable config fields plus a thread-safe `RandomNumberGenerator`, so `AddSingleton` won't cause bugs here. It's just a deliberate deviation from `AddIdentityCore`'s own default (`TryAddScoped`) — worth a one-line "why" for a future reader, not a fix.
- **Fix**: Optional — add a short parenthetical to Phase 2 item 5's contract noting Singleton is safe here because `PasswordHasher<TUser>` holds no per-request mutable state.
- **Decision**: ACCEPTED
