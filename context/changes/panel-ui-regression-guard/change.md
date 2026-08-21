---
change_id: panel-ui-regression-guard
title: Panel UI regression guard for cross-license race and theme toggle
status: impl_reviewed
created: 2026-08-19
updated: 2026-08-21
archived_at: null
---

## Notes

Phase 3 z context/foundation/test-plan.md: "Panel UI regression guard" — pokrywa ryzyka #6 (cross-license race na deactivate/reactivate confirmation) i #7 (theme toggle silently no-ops przez granicę render-mode). Testy: component (bUnit) dla #6, E2E (Playwright) dla #7 — E2E część już zrealizowana w trybie standalone przez /10x-e2e (e2e/theme-toggle.spec.ts, e2e/auth.setup.ts, e2e/playwright.config.ts naprawiony, aria-label dodany w AppBarActions.razor) i zweryfikowana deliberate-break testem, ale bez change foldera/Progress — trzeba to teraz sformalizować w change.md tego change'a.
