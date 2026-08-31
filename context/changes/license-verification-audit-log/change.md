---
change_id: license-verification-audit-log
title: License verification audit log
status: impl_reviewed
created: 2026-08-31
updated: 2026-08-31
archived_at: null
---

## Notes

**2026-08-31 — opened directly at `/10x-research`.** Roadmap slice `S-07` (added to
`context/foundation/roadmap.md` the same day, Stream F). Post-MVP scope addition with no backing
FR in PRD v1 — see `roadmap.md` S-07 PRD-refs note and Open Roadmap Questions; a matching FR (or an
explicit "extends beyond v1" note) should land in `prd.md` before or during `/10x-plan`.

Scope decisions locked with the user during this research session:

- **Collection only.** Detecting over-use (a license used by more than the one process it's meant
  for) is a *separate future issue*. This change only records the per-call trail.
- **No client-contract change.** The client keeps sending just the `X-Api-Key` header (the licence
  key). Model is **1 license = 1 process**, so the key already identifies the one legitimate
  caller — no separate installation identifier (consistent with the FR-009 Socrates deferral of
  "identyfikator instalacji / heartbeat").
- **MAC address is off the table** — not observable server-side, randomised/synthetic on modern
  OSes and in containers, strong PII.

`research.md` below has the full findings.
