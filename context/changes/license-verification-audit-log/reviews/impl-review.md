<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: License Verification Audit Log

- **Plan**: `context/changes/license-verification-audit-log/plan.md`
- **Scope**: Phases 1–3 of 3 (full plan)
- **Date**: 2026-08-31
- **Verdict**: NEEDS ATTENTION → APPROVED after triage (F1, F2, F3, F5 fixed; F4 skipped)
- **Findings**: 0 critical · 2 warnings · 3 observations

## Verdicts

| Dimension | Verdict | After triage |
|-----------|---------|--------------|
| Plan Adherence | PASS | PASS |
| Scope Discipline | PASS | PASS |
| Safety & Quality | WARNING | PASS (F1/F2 fixed) |
| Architecture | PASS | PASS |
| Pattern Consistency | PASS | PASS |
| Success Criteria | PASS | PASS |

## Grounding

Automated success criteria re-run at review time: `dotnet build` (both projects) clean ·
`dotnet format --verify-no-changes` exit 0 (both) · `dotnet ef migrations
has-pending-model-changes` → "No changes … since the last migration" · full suite **39/39
green**. Manual items 1.5–1.6, 2.9–2.12, 3.5–3.9 marked `[x]` and user-attested at each phase
gate (UI/ops checks with no diff evidence, as expected).

Diff (`434b12f^..HEAD`, 4 commits) matches the plan's file list exactly — no unplanned
production files. Both review sub-agents independently confirmed: nothing from "What We're NOT
Doing" leaked (no detection logic, no seat limit on `License`, no client change, no IP
hashing/geo, `AddAuditLogEntries()` byte-for-byte unchanged, `AuditLog` not reused,
`PanelHome` pagination untouched).

## Findings

### F1 — Retention service can crash the host on a bad `Verification:RetentionSweepInterval`

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: `src/Data/Verification/VerificationEventRetentionService.cs:21,27,34`
- **Detail**: `ExecuteAsync` reads `config.GetValue<TimeSpan>("Verification:RetentionSweepInterval", …)`
  at line 21 **outside** the `try` — an unparseable value throws `InvalidOperationException`,
  which faults `ExecuteAsync`. `new PeriodicTimer(interval)` at line 27 is inside the `try`, but
  the `catch` only handles `OperationCanceledException`, so a config of `"00:00:00"` or a
  negative `TimeSpan` throws `ArgumentOutOfRangeException` and escapes. Either way, with the
  default `BackgroundServiceExceptionBehavior.StopHost`, the whole app — including the verify
  API — goes down. This contradicts the plan's Critical Implementation Detail ("a failed sweep
  must not crash the host") and the design goal that audit infrastructure never takes the verify
  path down. It's also inconsistent with the careful `RetentionDays < 1` guard right below it.
  (Same class of issue, lower impact: `VerificationEventQueue` ctor's
  `config.GetValue("Verification:QueueCapacity", 10_000)` → `Channel.CreateBounded` throws at
  Singleton resolution on a `0`/negative/unparseable value — a loud startup failure, not a
  running-host crash.)
- **Fix**: Read the interval inside the `try`; validate `interval > TimeSpan.Zero` and fall back
  to the 6h default with a Warning when it isn't; broaden the `ExecuteAsync` catch to
  `catch (Exception)` (log + return). Apply the same defensive `catch (Exception)` to
  `VerificationEventWriter.ExecuteAsync` for consistency, and clamp `QueueCapacity` to a sane
  floor in the queue ctor.
  - Strength: Closes the crash-the-host path the plan explicitly disallows; matches the
    `RetentionDays` guard already in the same file.
  - Tradeoff: A few lines across two services; a misconfigured interval now silently falls back
    instead of failing fast.
  - Confidence: HIGH — `BackgroundServiceExceptionBehavior.StopHost` is the documented .NET 6+
    default and no `HostOptions` override exists in `Program.cs`.
  - Blind spot: None significant.
- **Decision**: FIXED — config validation + broadened catches in both BackgroundServices + QueueCapacity floor

### F2 — `StopAsync` drain can become a second concurrent reader on a `SingleReader` channel

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `src/Data/Verification/VerificationEventWriter.cs:35-45`
- **Detail**: `StopAsync` calls `base.StopAsync(cancellationToken)` and then loops
  `while (queue.Reader.Count > 0) await DrainOnceAsync(...)`, per a comment asserting
  `ExecuteAsync` has already exited. But `base.StopAsync` returns on
  `WhenAny(executeTask, Delay(∞, cancellationToken))` — on a host shutdown-timeout (default 30s,
  e.g. `ExecuteAsync` stuck in a slow `SaveChangesAsync`) it returns while `ExecuteAsync` is
  still reading the channel, so the drain loop becomes a second concurrent reader on a
  `SingleReader = true` channel (undefined behavior). Narrow — only on shutdown timeout, when
  the host is being force-killed anyway — but the invariant the comment claims does not strictly
  hold.
- **Fix**: Guard the drain: `if (ExecuteTask?.IsCompleted == true) { while (queue.Reader.Count > 0) … }`
  (`BackgroundService.ExecuteTask` is a `protected` property). Update the comment to match.
- **Decision**: FIXED — StopAsync drain guarded by ExecuteTask?.IsCompleted == true

### F3 — `Verification:*` config values are unvalidated

- **Severity**: 🔍 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: `VerificationEventQueue.cs:27`, `VerificationEventRetentionService.cs:21,45`
- **Detail**: `QueueCapacity` (default 10000) and `RetentionSweepInterval` (default 6h) are read
  with no bounds check. `RetentionDays` has the `< 1` guard but no upper bound. Defaults are all
  safe; the exposure is operator misconfiguration only. Largely subsumed by F1's fix — listed
  separately so the queue-capacity floor isn't forgotten.
- **Fix**: Clamp `QueueCapacity` to `>= 1` (or a higher floor) in the queue ctor; covered
  otherwise by F1.
- **Decision**: FIXED — subsumed by the F1 fix (Math.Max(1, QueueCapacity) + interval fallback)

### F4 — FK `OnDelete: Cascade` would silently drop verification history on a license hard-delete

- **Severity**: 🔍 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Architecture
- **Location**: `src/Data/LassieDbContext.cs:39-43`; migration `:31-36`
- **Detail**: The plan specified `Cascade` ("keep the schema honest"). There is no license
  hard-delete path today (only deactivate), so it's inert. If one is ever added, a license's
  verification-audit rows vanish with it while its `AuditLog` rows (no FK) survive — a minor
  future asymmetry. `Restrict` would instead force the question to be answered deliberately when
  a delete path is designed.
- **Fix**: Optional — change `OnDelete` to `Restrict` and regenerate the migration, or leave as
  planned and note it for whoever adds a delete path.
- **Decision**: SKIPPED — Cascade kept as planned; noted for whoever adds a license hard-delete path

### F5 — Two cosmetic deviations from the plan's text

- **Severity**: 🔍 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Plan Adherence
- **Location**: `VerificationEventRetentionService.cs:62`; `LicenseVerifications.razor:32`
- **Detail**: (a) The Information "deleted N" log fires only when `deleted > 0` — the plan said
  log unconditionally; effect is that a zero-delete sweep is silent. (b) The Occurred cell
  renders `"yyyy-MM-dd HH:mm:ss"` without the literal `'UTC'` suffix the plan's format string
  carried; the value is a correct UTC conversion (`.OccurredAtUtc.UtcDateTime`) and the column
  header reads "Occurred (UTC)", so it's unambiguous.
- **Fix**: Optional — drop the `> 0` guard on the log line; append `' UTC'` to the cell format
  string. Neither affects behavior.
- **Decision**: FIXED — unconditional retention log; Occurred cell now renders the ' UTC' suffix
