---
change_id: testing-backend-critical-path-coverage
title: Backend test bootstrap: status precedence, key secrecy, audit integrity
status: implemented
created: 2026-08-18
updated: 2026-08-18
archived_at: null
---

## Notes

Rollout Phase 1 of `context/foundation/test-plan.md` §3: "Backend critical-path
coverage". Bootstraps the project's first test project (no test infra exists
yet) and defends the three highest-severity, purely-backend risks from §2:

- **Risk #1** — status precedence miscomputed at a boundary (e.g. expiry
  exactly "now"). Prove: status computation returns the PRD-defined
  precedence (Deactivated > Expired > Active) across a boundary-inclusive
  matrix, sourced from the PRD/roadmap rule — not from reading current
  output (oracle problem).
- **Risk #2** — generated API key leaks in plaintext after the initial
  reveal-once display. Prove: no panel render, API response, or log line
  ever contains the raw key after generation — assert structural properties
  (persisted value != raw value), not the implementation's own hash output.
- **Risk #5** — a future write path skips load-before-mutate and silently
  corrupts the audit "before" snapshot. Prove: for the current License write
  path, the audit log's "before" snapshot reflects true persisted prior
  state, using a real/test DbContext comparing loaded-then-modified vs.
  attach-and-mark-dirty — not a mocked ChangeTracker.

Test types planned: unit + integration (xUnit).

Full risk map and Risk Response Guidance table: `context/foundation/test-plan.md` §2.
This change folder's `research.md` should ground the exact code paths and
anchors for these three risks; the test plan deliberately omits them
(signal, not knowledge — see test-plan.md §1 principle #3).
